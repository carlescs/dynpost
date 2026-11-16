using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Dynpost.Application.Common;
using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;

namespace Dynpost.Infrastructure.Interchange;

/// <summary>
/// JMeter test plans (<c>.jmx</c>). The plan's thread groups and controllers become folders and
/// its HTTP samplers requests. Header managers, HTTP Request Defaults (as a <c>{{baseUrl}}</c>
/// variable), authorization managers and User Defined Variables become folder settings; JSON
/// Extractors become captures. <c>${name}</c> references become <c>{{name}}</c>, and the JMeter
/// functions Dynpost has built-in variables for are rewritten. Everything else (scripts, logic
/// controllers, listeners, what a run plan can't express…) is listed in the warnings.
/// </summary>
/// <remarks>
/// A plan is XML in which each element is followed by a <c>hashTree</c> holding its children.
/// Config elements apply to their siblings and everything below them, so they are read level by
/// level. Each thread group also becomes a run plan (<see cref="ImportedRunPlan"/>): its threads,
/// ramp-up and loops or duration, timers as think time, throughput timers as a request rate, its
/// CSV Data Set, the order its random or throughput controllers give, and the assertions a plan can
/// check.
/// </remarks>
internal static class JMeterFormat
{
    public const string FormatName = "JMeter test plan";

    /// <summary>The variable HTTP Request Defaults become; another name is used if the plan defines it.</summary>
    public const string BaseUrlVariable = "baseUrl";

    private const string DefaultPlanName = "Test Plan";

    /// <summary>Whether the text is XML rather than JSON or YAML (it starts with <c>&lt;</c>).</summary>
    public static bool IsXml(string text) => text.AsSpan().TrimStart().TrimStart('﻿').StartsWith("<");

    /// <summary>Reads a test plan, or says why the XML can't be read as one.</summary>
    public static Result<ParsedCollection> Import(string text, string fileName)
    {
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                // No DTDs and no external resources: a plan never needs them (XXE).
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var reader = XmlReader.Create(new StringReader(text.TrimStart('﻿')), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"The file isn't valid XML: {ex.Message}");
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "jmeterTestPlan")
        {
            return new Error(
                ErrorCodes.InvalidFile,
                $"Dynpost doesn't recognize this XML file. It can import {CollectionImporter.SupportedFormats}.");
        }

        var importer = new Importer(Path.GetFileNameWithoutExtension(fileName));
        return importer.Read(root);
    }

    /// <summary>One test plan being read.</summary>
    private sealed class Importer
    {
        private readonly string _fallbackName;
        private readonly Notes _notes = new();
        private readonly List<string> _warnings = [];
        private readonly VariableRewriter _rewriter;
        private string _baseUrlVariable = BaseUrlVariable;

        /// <summary>Variables for the whole plan: User Defined Variables and property defaults.</summary>
        private readonly List<KeyValueEntry> _planVariables = [];

        /// <summary>Timers, throughput, CSV data and assertions of the test plan, for every thread group.</summary>
        private readonly LoadSettings _planWide = new();

        /// <summary>The run plans of the thread groups, in the plan's order.</summary>
        private readonly List<PlanBuilder> _plans = [];

        /// <summary>The thread group being read, or <c>null</c> outside one.</summary>
        private PlanBuilder? _plan;

        /// <summary>Controllers whose logic a run plan's order keeps, so they get no warning.</summary>
        private readonly HashSet<XElement> _mapped = [];

        public Importer(string fallbackName)
        {
            _fallbackName = fallbackName;
            _rewriter = new VariableRewriter(_notes, _planVariables);
        }

        public Result<ParsedCollection> Read(XElement root)
        {
            var tree = Children(root).FirstOrDefault();
            var plan = tree.Element;
            if (plan is null || plan.Name.LocalName != "TestPlan")
            {
                return new Error(ErrorCodes.InvalidFile, "The JMeter file has no test plan.");
            }

            // User Defined Variables apply to the whole plan wherever they are, so they are read
            // first: their names decide the base URL variable's.
            CollectVariables(Elem(plan, "TestPlan.user_defined_variables"));
            foreach (var element in root.Descendants().Where(e => e.Name.LocalName == "Arguments" && Enabled(e)))
            {
                CollectVariables(element);
            }

            if (_planVariables.Any(v => v.Key == BaseUrlVariable))
            {
                _baseUrlVariable = "jmeterBaseUrl";
            }

            var name = plan.Attribute("testname")?.Value.Trim();
            if (string.IsNullOrEmpty(name) || name == DefaultPlanName)
            {
                name = _fallbackName.Length > 0 ? _fallbackName : DefaultPlanName;
            }

            var folder = ReadLevel(name, tree.Tree, Scope.Root, LevelKind.Plan) ?? new CollectionFolder(name, [], []);
            var runPlans = _plans.Select(BuildPlan).OfType<ImportedRunPlan>().ToList();
            var rootVariables = _planVariables.Select(v => v with { Value = _rewriter.Rewrite(v.Value, $"the variable '{v.Key}'") }).ToList();

            // The rewriter may have found property defaults (__P) while reading the requests.
            rootVariables.AddRange(_rewriter.PropertyDefaults.Where(p => rootVariables.All(v => v.Key != p.Key)));
            var defaults = folder.Defaults with { Variables = [.. rootVariables, .. folder.Defaults.Variables] };
            folder = folder with { Defaults = defaults };

            _warnings.AddRange(_notes.Lines());
            var properties = _rewriter.PropertiesWithoutDefault.Where(p => rootVariables.All(v => v.Key != p)).ToList();
            if (properties.Count > 0)
            {
                _warnings.Add($"The plan reads JMeter properties without a default value; set them in an environment: {string.Join(", ", properties)}.");
            }

            return new ParsedCollection(FormatName, new RequestCollection(folder, []) { Plans = runPlans }, _warnings);
        }

        private void CollectVariables(XElement? arguments)
        {
            foreach (var argument in Items(arguments, "Arguments.arguments"))
            {
                var key = Str(argument, "Argument.name") ?? argument.Attribute("name")?.Value ?? string.Empty;
                var value = Str(argument, "Argument.value") ?? string.Empty;
                if (key.Length > 0 && _planVariables.All(v => v.Key != key))
                {
                    _planVariables.Add(new KeyValueEntry(key, value));
                }
            }
        }

        /// <summary>
        /// Reads the children of a folder-like element (the plan, a thread group, a controller)
        /// into a folder; <c>null</c> when nothing in it was imported.
        /// </summary>
        private CollectionFolder? ReadLevel(string name, XElement? tree, Scope parent, LevelKind levelKind)
        {
            var children = Children(tree).ToList();
            var level = parent.Inner();
            var defaults = FolderDefaults.Empty;

            // Load settings at the plan's or a thread group's level apply to the whole run plan.
            var load = levelKind switch
            {
                LevelKind.Plan => _planWide,
                LevelKind.ThreadGroup => _plan?.Settings,
                _ => null,
            };
            if (levelKind == LevelKind.ThreadGroup && _plan is not null)
            {
                _plan.Order = PlanOrder(children);
            }

            // Config elements first: they apply to every sibling, whatever its position.
            foreach (var (element, _) in children.Where(c => Enabled(c.Element)))
            {
                switch (Kind(element))
                {
                    case ElementKind.HttpDefaults:
                        var http = HttpFields.Read(element);
                        level.Defaults.Add(http);
                        if (http.Timeout() is { } timeout)
                        {
                            defaults = defaults with { Settings = defaults.Settings with { Timeout = timeout } };
                        }

                        break;
                    case ElementKind.Headers:
                        var headers = Headers(element);
                        level.Headers.AddRange(headers);
                        defaults = defaults with { Headers = Merge(defaults.Headers, headers) };
                        break;
                    case ElementKind.Auth:
                        var manager = AuthManager.Read(element, _notes);
                        level.Auth.Add(manager);
                        if (manager.Universal is { } universal)
                        {
                            defaults = defaults with { Auth = universal };
                        }

                        break;
                    case ElementKind.JsonExtractor:
                        level.Captures.AddRange(Captures(element));
                        break;
                    case ElementKind.Assertion when load is null:
                        level.Assertions.Add(Assertion(element));
                        break;
                    case ElementKind.Timer or ElementKind.ThroughputTimer or ElementKind.Assertion or ElementKind.CsvData:
                        ReadLoadElement(element, load);
                        break;
                }
            }

            // The base URL of HTTP Request Defaults at this level, closer ones replacing outer ones.
            var effective = level.EffectiveDefaults();
            if (level.Defaults.Any(d => d.Domain.Length > 0 || d.Protocol.Length > 0 || d.Port.Length > 0) && effective.Domain.Length > 0)
            {
                defaults = defaults with { Variables = [new KeyValueEntry(_baseUrlVariable, effective.Origin())] };
            }

            var folders = new List<CollectionFolder>();
            var requests = new List<HttpRequestDefinition>();
            foreach (var (element, subtree) in children)
            {
                var kind = Kind(element);
                if (levelKind == LevelKind.ThreadGroup && _plan is not null)
                {
                    _plan.CurrentTop = element;
                }

                // A disabled request of a thread group is imported, and disabled in its run plan.
                var enabled = Enabled(element);
                if (!enabled && !(kind == ElementKind.Sampler && _plan is not null))
                {
                    _notes.Add("Skipped disabled elements", Label(element));
                    continue;
                }

                switch (kind)
                {
                    case ElementKind.Sampler:
                        if (ReadRequest(element, subtree, level) is { } read)
                        {
                            requests.Add(read.Request);
                            _plan?.Requests.Add(new PlanRequest(read.Request, enabled, _plan.CurrentTop, read.Assertion));
                            if (!enabled)
                            {
                                _notes.Add("Imported disabled requests; they are disabled in their run plan", $"'{read.Request.Name}'");
                            }
                        }

                        break;
                    case ElementKind.ThreadGroup:
                        _plan = StartPlan(element);
                        var group = ReadLevel(TestName(element), subtree, level, LevelKind.ThreadGroup);
                        if (_plan is not null && group is not null)
                        {
                            _plans.Add(_plan);
                        }

                        _plan = null;
                        if (group is not null)
                        {
                            folders.Add(group);
                        }

                        break;
                    case ElementKind.Controller:
                    case ElementKind.LogicController:
                        if (kind == ElementKind.LogicController && !_mapped.Contains(element))
                        {
                            _notes.Add("Imported as plain folders; Dynpost doesn't run JMeter's logic controllers", Label(element));
                        }

                        if (ReadLevel(TestName(element), subtree, level, LevelKind.Controller) is { } folder)
                        {
                            folders.Add(folder);
                        }

                        break;
                    default:
                        NoteSkipped(element, kind);
                        break;
                }
            }

            if (levelKind != LevelKind.Plan && requests.Count == 0 && folders.Count == 0)
            {
                return null;
            }

            defaults = defaults with
            {
                Headers = defaults.Headers.Select(h => h with { Key = _rewriter.Rewrite(h.Key, name), Value = _rewriter.Rewrite(h.Value, name) }).ToList(),
                Auth = RewriteAuth(defaults.Auth, name),
                Variables = defaults.Variables.Select(v => v with { Value = _rewriter.Rewrite(v.Value, name) }).ToList(),
            };

            return new CollectionFolder(name, folders, requests) { Defaults = defaults };
        }

        private (HttpRequestDefinition Request, RunAssertion? Assertion)? ReadRequest(XElement sampler, XElement? tree, Scope parent)
        {
            var name = TestName(sampler);
            var own = parent.Inner();
            var ownHeaders = new List<KeyValueEntry>();
            var assertions = new List<RunAssertion?>();
            foreach (var (element, _) in Children(tree))
            {
                var kind = Kind(element);
                if (!Enabled(element))
                {
                    _notes.Add("Skipped disabled elements", Label(element));
                    continue;
                }

                switch (kind)
                {
                    case ElementKind.Headers:
                        ownHeaders = Merge(ownHeaders, Headers(element));
                        break;
                    case ElementKind.Auth:
                        own.Auth.Add(AuthManager.Read(element, _notes) with { Universal = null });
                        break;
                    case ElementKind.JsonExtractor:
                        own.Captures.AddRange(Captures(element));
                        break;
                    case ElementKind.HttpDefaults:
                        own.Defaults.Add(HttpFields.Read(element));
                        break;
                    case ElementKind.Assertion:
                        assertions.Add(Assertion(element));
                        break;
                    case ElementKind.Timer or ElementKind.ThroughputTimer or ElementKind.CsvData:
                        ReadLoadElement(element, null);
                        break;
                    default:
                        NoteSkipped(element, kind);
                        break;
                }
            }

            var http = HttpFields.Read(sampler);
            var methodName = http.Method.Length == 0 ? "GET" : http.Method;
            if (!RequestMethods.TryParse(methodName, out var method))
            {
                _notes.Add("Skipped requests with HTTP methods Dynpost doesn't support", $"'{name}' ({methodName.ToUpperInvariant()})");
                return null;
            }

            var defaults = own.EffectiveDefaults();
            var (rawUrl, literalUrl) = Url(http, defaults, name);
            var (url, query) = InterchangeText.SplitQuery(rawUrl);

            var arguments = http.Arguments
                .Concat(defaults.Arguments.Where(d => d.Name.Length > 0 && http.Arguments.All(a => a.Name != d.Name)))
                .ToList();
            var contentType = ownHeaders.Concat(Enumerable.Reverse(own.AllHeaders()))
                .FirstOrDefault(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
            var sendsBody = method is RequestMethod.Post or RequestMethod.Put or RequestMethod.Patch;
            var body = Body(http, arguments, sendsBody, contentType, name);
            if (body.Type == BodyType.None && !http.RawBody && !http.Multipart)
            {
                query.AddRange(arguments.Where(a => a.Name.Length > 0).Select(a => new KeyValueEntry(a.Name, a.Value)));
            }

            var request = new HttpRequestDefinition(
                name,
                method,
                url,
                query,
                InterchangeText.WithoutImpliedContentType(ownHeaders, body.Type),
                own.AuthFor(literalUrl),
                body)
            {
                Settings = new RequestSettings(Timeout: http.Timeout(), FollowRedirects: http.FollowRedirects),
                Captures = own.AllCaptures(),
                Description = Comments(sampler),
            };

            var assertion = MergeAssertions([.. parent.AllAssertions(), .. assertions], name);
            return (InterchangeText.MapText(request, text => _rewriter.Rewrite(text, $"'{name}'")), assertion);
        }

        /// <summary>
        /// A run plan for a thread group, with its threads, ramp-up and iterations or duration;
        /// <c>null</c> (with a warning) for thread groups a run plan can't stand for.
        /// </summary>
        private PlanBuilder? StartPlan(XElement group)
        {
            var name = TestName(group);
            var tag = group.Name.LocalName;
            int threads;
            TimeSpan rampUp;
            int? iterations = null;
            TimeSpan? duration = null;
            switch (tag)
            {
                case "ThreadGroup":
                    threads = (int)Math.Round(Number(Str(group, "ThreadGroup.num_threads")) ?? 1);
                    rampUp = Seconds(Number(Str(group, "ThreadGroup.ramp_time")));
                    var loops = Number(Str(Elem(group, "ThreadGroup.main_controller"), "LoopController.loops")) ?? 1;
                    var scheduled = Bool(group, "ThreadGroup.scheduler") == true ? Number(Str(group, "ThreadGroup.duration")) : null;
                    if (loops > 0)
                    {
                        iterations = (int)Math.Min(Math.Round(loops), RunPlan.MaxIterations);
                        if (scheduled is > 0)
                        {
                            _notes.Add("Thread groups with both a loop count and a duration; their run plan counts iterations", $"'{name}'");
                        }
                    }
                    else if (scheduled is > 0)
                    {
                        duration = Seconds(scheduled);
                    }
                    else
                    {
                        duration = DefaultDuration;
                        _notes.Add($"Thread groups that loop forever; their run plan runs for {RunText.FormatDuration(DefaultDuration)}", $"'{name}'");
                    }

                    break;
                case "com.blazemeter.jmeter.threads.concurrency.ConcurrencyThreadGroup":
                    var minutes = Str(group, "Unit") == "M";
                    TimeSpan Time(string key) => minutes ? TimeSpan.FromMinutes(Number(Str(group, key)) ?? 0) : Seconds(Number(Str(group, key)));
                    threads = (int)Math.Round(Number(Str(group, "TargetLevel")) ?? 1);
                    rampUp = Time("RampUp");
                    duration = rampUp + Time("Hold");
                    _notes.Add("Plugin thread groups approximated by threads, ramp-up and duration", Label(group));
                    break;
                case "kg.apc.jmeter.threads.UltimateThreadGroup":
                    var rows = Prop(group, "ultimatethreadgroupdata")?.Elements()
                        .Select(row => row.Elements().Select(cell => Number(cell.Value) ?? 0).ToList())
                        .Where(row => row.Count >= 4)
                        .ToList() ?? [];
                    threads = (int)rows.Sum(row => row[0]);
                    rampUp = Seconds(rows.Select(row => row[2]).DefaultIfEmpty(0).Max());
                    duration = Seconds(rows.Select(row => row.Take(5).Skip(1).Sum()).DefaultIfEmpty(0).Max());
                    _notes.Add("Plugin thread groups approximated by threads, ramp-up and duration", Label(group));
                    break;
                default:
                    _notes.Add("Thread groups not turned into run plans (setUp, tearDown and arrival-based ones)", Label(group));
                    return null;
            }

            if (threads > RunPlan.MaxThreads)
            {
                _notes.Add($"Thread groups with more than {RunPlan.MaxThreads} threads; their run plan uses {RunPlan.MaxThreads}", $"'{name}'");
            }

            if (duration > RunPlan.MaxDuration)
            {
                duration = RunPlan.MaxDuration;
            }

            if (duration <= TimeSpan.Zero)
            {
                duration = DefaultDuration;
            }

            var load = new RunLoad(Math.Clamp(threads, 1, RunPlan.MaxThreads), iterations, duration, rampUp, TimeSpan.Zero, TimeSpan.Zero, null);
            return new PlanBuilder(name.Length > 0 ? name : "Thread Group", load);
        }

        /// <summary>
        /// How a thread group's virtual users take its requests: a single Random Order Controller
        /// shuffles them, a single Random Controller picks one, and Throughput Controllers in
        /// percent mode pick one by their percentages.
        /// </summary>
        private RequestOrder PlanOrder(List<(XElement Element, XElement? Tree)> children)
        {
            var units = children
                .Where(c => Enabled(c.Element) && Kind(c.Element) is ElementKind.Sampler or ElementKind.Controller or ElementKind.LogicController)
                .Select(c => c.Element)
                .ToList();
            if (units is [{ Name.LocalName: "RandomOrderController" } shuffled])
            {
                _mapped.Add(shuffled);
                return RequestOrder.Shuffle;
            }

            if (units is [{ Name.LocalName: "RandomController" } random])
            {
                _mapped.Add(random);
                return RequestOrder.Random;
            }

            if (units.Count > 0 && units.All(u => u.Name.LocalName == "ThroughputController" && Str(u, "ThroughputController.style") == "1"))
            {
                _mapped.UnionWith(units);
                return RequestOrder.Random;
            }

            return RequestOrder.Sequential;
        }

        /// <summary>A timer, throughput timer, assertion or CSV Data Set for a run plan; <paramref name="load"/> is <c>null</c> below its level.</summary>
        private void ReadLoadElement(XElement element, LoadSettings? load)
        {
            var kind = Kind(element);
            // A CSV Data Set anywhere in a thread group feeds its run plan.
            if (load is null && kind == ElementKind.CsvData && _plan is not null)
            {
                load = _plan.Settings;
            }

            if (load is null)
            {
                _notes.Add(
                    kind == ElementKind.CsvData
                        ? "Not imported: CSV Data Sets outside thread groups that become run plans"
                        : "Not imported: timers inside controllers or requests, or in thread groups that don't become run plans (a run plan's think time and rate apply to all its requests)",
                    Label(element));
                return;
            }

            switch (kind)
            {
                case ElementKind.Timer:
                    if (Timer(element) is { } timer)
                    {
                        load.Timers.Add(timer);
                    }

                    break;
                case ElementKind.ThroughputTimer:
                    ReadThroughput(element, load);
                    break;
                case ElementKind.Assertion:
                    load.Assertions.Add(Assertion(element));
                    break;
                case ElementKind.CsvData:
                    if (load.Data is not null)
                    {
                        _notes.Add("Only the first CSV Data Set of a thread group is used", Label(element));
                    }
                    else
                    {
                        load.Data = DataFile(element);
                    }

                    break;
            }
        }

        private (TimeSpan Min, TimeSpan Max)? Timer(XElement element)
        {
            var delay = Number(Str(element, "ConstantTimer.delay"));
            var range = Number(Str(element, "RandomTimer.range")) ?? 0;
            if (delay is null || delay < 0 || range < 0)
            {
                _notes.Add("Not imported: timers whose delay isn't a number", Label(element));
                return null;
            }

            switch (element.Name.LocalName)
            {
                case "UniformRandomTimer":
                    return (TimeSpan.FromMilliseconds(delay.Value), TimeSpan.FromMilliseconds(delay.Value + range));
                case "GaussianRandomTimer":
                    _notes.Add("Gaussian timers approximated by a uniform think time of the delay plus or minus the deviation", Label(element));
                    return (TimeSpan.FromMilliseconds(Math.Max(0, delay.Value - range)), TimeSpan.FromMilliseconds(delay.Value + range));
                default:
                    return (TimeSpan.FromMilliseconds(delay.Value), TimeSpan.FromMilliseconds(delay.Value));
            }
        }

        private void ReadThroughput(XElement element, LoadSettings load)
        {
            var throughput = Number(Value(element, "throughput"));
            if (throughput is not > 0)
            {
                _notes.Add("Not imported: throughput timers whose target isn't a number", Label(element));
                return;
            }

            if (element.Name.LocalName == "PreciseThroughputTimer")
            {
                var period = Number(Value(element, "throughputPeriod")) ?? 3600;
                load.Rates.Add((throughput.Value / Math.Max(period, 1), false));
                return;
            }

            // Constant Throughput Timer: samples per minute, for one thread (calcMode 0) or all of them.
            var perThread = Value(element, "calcMode") is null or "0";
            if (perThread)
            {
                _notes.Add("Per-thread throughput targets approximated by a total rate (target × threads)", Label(element));
            }

            load.Rates.Add((throughput.Value / 60, perThread));
        }

        private ImportedDataFile? DataFile(XElement element)
        {
            var file = (Str(element, "filename") ?? string.Empty).Trim();
            if (file.Length == 0 || file.Contains("${", StringComparison.Ordinal))
            {
                _notes.Add("Not imported: CSV Data Sets whose file name is empty or uses variables", Label(element));
                return null;
            }

            var delimiter = Str(element, "delimiter") ?? ",";
            delimiter = delimiter is "\\t" or "\t" ? "\t" : delimiter.Length == 0 ? "," : delimiter;
            var names = (Str(element, "variableNames") ?? string.Empty).Trim();
            string? header = null;
            if (names.Length > 0)
            {
                var list = names.Split(delimiter).Select(n => n.Trim()).ToList();
                if (list.Any(n => !VariableTemplate.IsValidName(n)))
                {
                    _notes.Add("Not imported: CSV Data Sets with variable names Dynpost can't use", $"{Label(element)}: {names}");
                    return null;
                }

                header = string.Join(',', list);
            }

            var mode = Str(element, "shareMode") switch
            {
                "shareMode.thread" => SelectionMode.PerThread,
                _ => SelectionMode.Sequential,
            };
            var onEof = Bool(element, "recycle") == false ? EndBehavior.Stop : EndBehavior.Recycle;
            return new ImportedDataFile(file, mode, onEof, header, header is not null && Bool(element, "ignoreFirstLine") == true, delimiter);
        }

        /// <summary>
        /// The checks of a Response Assertion (status codes, or text the body contains or doesn't)
        /// or a Duration Assertion; <c>null</c> (with a warning) for checks a run plan can't express.
        /// </summary>
        private RunAssertion? Assertion(XElement element)
        {
            if (element.Name.LocalName == "DurationAssertion")
            {
                var ms = Number(Str(element, "DurationAssertion.duration"));
                if (ms is > 0)
                {
                    return new RunAssertion([], TimeSpan.FromMilliseconds(ms.Value), null, null);
                }

                _notes.Add("Not imported: assertions a run plan can't check", Label(element));
                return null;
            }

            var field = Str(element, "Assertion.test_field");
            var type = (int)(Number(Str(element, "Assertion.test_type")) ?? 0);
            var strings = Prop(element, "Asserion.test_strings")?.Elements().Select(e => e.Value).Where(v => v.Length > 0).ToList() ?? [];
            const int Match = 1, Contains = 2, Not = 4, Equals = 8, Substring = 16;
            var negated = (type & Not) != 0;
            if (strings.Count > 0 && strings.All(v => !v.Contains("${", StringComparison.Ordinal)))
            {
                if (field == "Assertion.response_code" && !negated)
                {
                    var codes = strings.Select(StatusPattern).ToList();
                    if (codes.All(c => c is not null))
                    {
                        return new RunAssertion(codes!, null, null, null);
                    }
                }
                else if (field == "Assertion.response_data" && strings.Count == 1 && (type & Equals) == 0
                    && ((type & Substring) != 0 || ((type & (Match | Contains)) != 0 && !HasRegexSyntax(strings[0]))))
                {
                    return negated
                        ? new RunAssertion([], null, null, strings[0])
                        : new RunAssertion([], null, strings[0], null);
                }
            }

            _notes.Add("Not imported: assertions a run plan can't check (status codes, a body text it contains or doesn't, a time limit)", Label(element));
            return null;
        }

        private static string? StatusPattern(string text)
        {
            var value = text.Trim();
            if (value.Length == 3 && value.All(char.IsAsciiDigit) && value[0] is >= '1' and <= '5')
            {
                return value;
            }

            foreach (var suffix in new[] { "xx", "XX", "..", "\\d\\d", "[0-9][0-9]" })
            {
                if (value.Length == 1 + suffix.Length && value[0] is >= '1' and <= '5' && value.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return value[0] + "xx";
                }
            }

            return null;
        }

        private static bool HasRegexSyntax(string text) => text.IndexOfAny(['\\', '.', '*', '+', '?', '(', ')', '[', ']', '{', '}', '|', '^', '$']) >= 0;

        /// <summary>Several assertions as one: their status codes, the shortest time limit and the first texts.</summary>
        private RunAssertion? MergeAssertions(IReadOnlyList<RunAssertion?> assertions, string owner)
        {
            var list = assertions.OfType<RunAssertion>().ToList();
            if (list.Count == 0)
            {
                return null;
            }

            var contains = list.Select(a => a.BodyContains).OfType<string>().Distinct().ToList();
            var notContains = list.Select(a => a.BodyNotContains).OfType<string>().Distinct().ToList();
            if (contains.Count > 1 || notContains.Count > 1)
            {
                _notes.Add("Only the first body text check of a request is kept", owner);
            }

            return new RunAssertion(
                list.SelectMany(a => a.Status).Distinct().ToList(),
                list.Select(a => a.MaxTime).Where(t => t is not null).DefaultIfEmpty(null).Min(),
                contains.FirstOrDefault(),
                notContains.FirstOrDefault());
        }

        private ImportedRunPlan? BuildPlan(PlanBuilder builder)
        {
            if (builder.Requests.Count == 0)
            {
                return null;
            }

            var timers = _planWide.Timers.Concat(builder.Settings.Timers).ToList();
            var think = (Min: TimeSpan.FromTicks(timers.Sum(t => t.Min.Ticks)), Max: TimeSpan.FromTicks(timers.Sum(t => t.Max.Ticks)));
            var rates = _planWide.Rates.Concat(builder.Settings.Rates)
                .Select(rate => rate.PerThread ? rate.PerSecond * builder.Load.Threads : rate.PerSecond)
                .ToList();
            var load = builder.Load with
            {
                ThinkMin = think.Min,
                ThinkMax = think.Max,
                MaxRps = rates.Count == 0 ? null : Math.Round(rates.Min(), 3),
            };

            // Throughput Controllers share their percentage among their requests.
            var weights = builder.Order == RequestOrder.Random && builder.Requests.Any(r => r.Top?.Name.LocalName == "ThroughputController")
                ? builder.Requests.GroupBy(r => r.Top).ToDictionary(
                    g => g.Key!,
                    g => Math.Round((Number(Value(g.Key, "ThroughputController.percentThroughput")) ?? 100) / g.Count(), 3))
                : null;

            return new ImportedRunPlan(
                builder.Name,
                builder.Requests.Select(r => new ImportedPlanRequest(
                    r.Request,
                    r.Enabled,
                    weights is not null && r.Top is not null ? weights[r.Top] : null,
                    r.Assertion)).ToList(),
                load,
                builder.Order,
                MergeAssertions([.. _planWide.Assertions, .. builder.Settings.Assertions], $"'{builder.Name}'"),
                builder.Settings.Data ?? _planWide.Data);
        }

        /// <summary>
        /// A number written in the plan: plain, or a <c>${var}</c> User Defined Variable or a
        /// <c>${__P(name,default)}</c> property whose value or default is one; <c>null</c> otherwise.
        /// </summary>
        private double? Number(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            for (var depth = 0; depth < 5 && value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}'); depth++)
            {
                var inner = value[2..^1].Trim();
                if (inner.StartsWith("__P(", StringComparison.Ordinal) && inner.EndsWith(')'))
                {
                    var args = inner[4..^1].Split(',');
                    value = args.Length > 1 ? args[1].Trim() : string.Empty;
                }
                else if (inner.StartsWith("__property(", StringComparison.Ordinal) && inner.EndsWith(')'))
                {
                    var args = inner[11..^1].Split(',');
                    value = args.Length > 2 ? args[2].Trim() : string.Empty;
                }
                else
                {
                    value = _planVariables.FirstOrDefault(v => v.Key == inner)?.Value.Trim() ?? string.Empty;
                }
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                ? number
                : null;
        }

        private static TimeSpan Seconds(double? seconds) => seconds is > 0 ? TimeSpan.FromSeconds(Math.Min(seconds.Value, RunPlan.MaxDuration.TotalSeconds)) : TimeSpan.Zero;

        /// <summary>
        /// The request's URL as it is saved (with <c>{{baseUrl}}</c> when HTTP Request Defaults
        /// name the server) and as JMeter would send it, to match authorization URLs against.
        /// </summary>
        private (string Saved, string Literal) Url(HttpFields http, HttpFields defaults, string name)
        {
            var path = http.Path.Length > 0 ? http.Path : defaults.Path;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return (path, path);
            }

            if (path.Length == 0 || path[0] != '/')
            {
                path = "/" + path;
            }

            if (http.Domain.Length > 0)
            {
                var origin = http.Merge(defaults).Origin();
                return (origin + path, origin + path);
            }

            if (defaults.Domain.Length > 0)
            {
                return ("{{" + _baseUrlVariable + "}}" + path, defaults.Origin() + path);
            }

            _notes.Add("Requests without a server name (no domain and no HTTP Request Defaults); their URL is only the path", $"'{name}'");
            return (path, path);
        }

        private RequestBody Body(HttpFields http, List<Argument> arguments, bool sendsBody, string? contentType, string name)
        {
            if (http.Multipart || http.Files.Any(f => f.Name.Length > 0))
            {
                var fields = arguments
                    .Select(a => new FormField(a.Name, a.Value))
                    .Concat(http.Files.Where(f => f.Name.Length > 0).Select(f => FormField.File(f.Name, f.Path, f.MimeType)))
                    .ToList();
                if (http.Files.Any(f => f.Name.Length == 0))
                {
                    _notes.Add("Skipped files without a parameter name in multipart requests", $"'{name}'");
                }

                return InterchangeText.FormBody(fields);
            }

            if (http.Files.FirstOrDefault() is { } file)
            {
                if (http.Files.Count > 1)
                {
                    _notes.Add("Only the first file is sent as the body", $"'{name}'");
                }

                return new RequestBody(BodyType.Binary, file.Path);
            }

            // JMeter sends a single unnamed argument as the body, like a raw body.
            var raw = http.RawBody || (sendsBody && arguments.Count > 0 && arguments.All(a => a.Name.Length == 0));
            if (raw)
            {
                var text = string.Concat(arguments.Select(a => a.Value));
                if (text.Length == 0)
                {
                    return RequestBody.None;
                }

                var json = InterchangeText.IsJson(contentType) || (contentType is null && LooksLikeJson(text));
                return new RequestBody(json ? BodyType.Json : BodyType.Text, text);
            }

            if (sendsBody && arguments.Count > 0)
            {
                return RequestBody.WithFields(
                    BodyType.UrlEncoded,
                    arguments.Where(a => a.Name.Length > 0).Select(a => new FormField(a.Name, a.Value, Encode: a.Encode)));
            }

            return RequestBody.None;
        }

        private IEnumerable<CaptureRule> Captures(XElement extractor)
        {
            var label = Label(extractor);
            var names = (Str(extractor, "JSONPostProcessor.referenceNames") ?? string.Empty).Split(';');
            var paths = (Str(extractor, "JSONPostProcessor.jsonPathExprs") ?? string.Empty).Split(';');
            var matches = (Str(extractor, "JSONPostProcessor.match_numbers") ?? string.Empty).Split(';');
            for (var i = 0; i < names.Length; i++)
            {
                var variable = names[i].Trim();
                var path = i < paths.Length ? paths[i].Trim() : string.Empty;
                var match = i < matches.Length ? matches[i].Trim() : matches.Length == 1 ? matches[0].Trim() : string.Empty;
                if (variable.Length == 0 && path.Length == 0)
                {
                    continue;
                }

                if (!VariableTemplate.IsValidName(variable) || !path.StartsWith('$') || !CaptureExpression.TryParse(path, out _, out _) || match is not ("" or "0" or "1"))
                {
                    _notes.Add("JSON Extractors Dynpost can't capture with (captures take one value from a simple path such as $.data.id)", $"{label}: {path}");
                    continue;
                }

                yield return new CaptureRule(variable, path);
            }
        }

        private RequestAuth RewriteAuth(RequestAuth auth, string owner) => auth switch
        {
            BasicAuth basic => new BasicAuth(_rewriter.Rewrite(basic.Username, owner), _rewriter.Rewrite(basic.Password, owner)),
            DigestAuth digest => new DigestAuth(_rewriter.Rewrite(digest.Username, owner), _rewriter.Rewrite(digest.Password, owner)),
            _ => auth,
        };

        private void NoteSkipped(XElement element, ElementKind kind)
        {
            var reason = kind switch
            {
                ElementKind.Script => "Not imported: Dynpost doesn't run scripts",
                ElementKind.OtherSampler => "Not imported: Dynpost only imports HTTP requests",
                ElementKind.Extractor => "Not imported: only JSON Extractors become captures",
                ElementKind.LoadElement => "Not imported: timers and assertions run plans can't express",
                ElementKind.Timer or ElementKind.ThroughputTimer or ElementKind.Assertion or ElementKind.CsvData => null,
                ElementKind.Listener => "Not imported: listeners (Dynpost shows responses and run results itself)",
                ElementKind.Ignored => null,
                ElementKind.Variables => null,
                ElementKind.HttpDefaults or ElementKind.Headers or ElementKind.Auth or ElementKind.JsonExtractor => null,
                _ => "Not imported: elements Dynpost doesn't support",
            };
            if (reason is not null)
            {
                _notes.Add(reason, Label(element));
            }
        }

        private static List<KeyValueEntry> Headers(XElement manager) =>
            Items(manager, "HeaderManager.headers")
                .Select(h => new KeyValueEntry((Str(h, "Header.name") ?? string.Empty).Trim(), Str(h, "Header.value") ?? string.Empty))
                .Where(h => h.Key.Length > 0)
                .ToList();

        /// <summary>The headers of two managers at the same level: the later one's replace the earlier one's.</summary>
        private static List<KeyValueEntry> Merge(IReadOnlyList<KeyValueEntry> earlier, IReadOnlyList<KeyValueEntry> later) =>
        [
            .. earlier.Where(e => !later.Any(l => string.Equals(l.Key, e.Key, StringComparison.OrdinalIgnoreCase))),
            .. later,
        ];

        private static bool LooksLikeJson(string text)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith('{') || trimmed.StartsWith('[');
        }
    }

    /// <summary>What config elements in scope add to a request: from the plan down to the request.</summary>
    private sealed class Scope
    {
        public static Scope Root => new(null);

        private readonly Scope? _parent;

        private Scope(Scope? parent) => _parent = parent;

        public List<HttpFields> Defaults { get; } = [];

        public List<KeyValueEntry> Headers { get; } = [];

        public List<AuthManager> Auth { get; } = [];

        public List<CaptureRule> Captures { get; } = [];

        /// <summary>Assertions of controllers, which apply to each request below them.</summary>
        public List<RunAssertion?> Assertions { get; } = [];

        public Scope Inner() => new(this);

        /// <summary>The controllers' assertions in scope, the outermost first.</summary>
        public IEnumerable<RunAssertion?> AllAssertions() => OuterFirst().SelectMany(s => s.Assertions);

        private IEnumerable<Scope> OuterFirst() => _parent is null ? [this] : [.. _parent.OuterFirst(), this];

        /// <summary>HTTP Request Defaults merged from the outermost down; closer values win.</summary>
        public HttpFields EffectiveDefaults() =>
            OuterFirst().SelectMany(s => s.Defaults).Aggregate(HttpFields.Empty, (outer, inner) => inner.Merge(outer));

        /// <summary>The headers in scope, the outermost first.</summary>
        public IEnumerable<KeyValueEntry> AllHeaders() => OuterFirst().SelectMany(s => s.Headers);

        /// <summary>The captures in scope, the outermost first; a closer one replaces one for the same variable.</summary>
        public List<CaptureRule> AllCaptures()
        {
            var captures = new List<CaptureRule>();
            foreach (var capture in OuterFirst().SelectMany(s => s.Captures))
            {
                captures.RemoveAll(c => c.Variable == capture.Variable);
                captures.Add(capture);
            }

            return captures;
        }

        /// <summary>
        /// The auth of a request: the first authorization of the closest manager whose URL starts
        /// the request's. A manager that applies to everything sets its folder's auth instead, so
        /// requests below it inherit it.
        /// </summary>
        public RequestAuth AuthFor(string url)
        {
            foreach (var manager in OuterFirst().Reverse().SelectMany(s => Enumerable.Reverse(s.Auth)))
            {
                if (manager.Universal is not null)
                {
                    return RequestAuth.Inherit;
                }

                if (manager.Match(url) is { } auth)
                {
                    return auth;
                }
            }

            return RequestAuth.Inherit;
        }
    }

    /// <param name="Universal">
    /// The auth when the manager has a single Basic or Digest authorization for every URL, so it
    /// can be its folder's auth.
    /// </param>
    private sealed record AuthManager(IReadOnlyList<(string Url, RequestAuth Auth)> Entries, RequestAuth? Universal)
    {
        public static AuthManager Read(XElement element, Notes notes)
        {
            var entries = new List<(string Url, RequestAuth Auth)>();
            foreach (var item in Items(element, "AuthManager.auth_list"))
            {
                var url = Str(item, "Authorization.url") ?? string.Empty;
                var username = Str(item, "Authorization.username") ?? string.Empty;
                var password = Str(item, "Authorization.password") ?? string.Empty;
                var mechanism = (Str(item, "Authorization.mechanism") ?? "BASIC_DIGEST").ToUpperInvariant();
                RequestAuth? auth = mechanism switch
                {
                    "BASIC" or "BASIC_DIGEST" or "" => new BasicAuth(username, password),
                    "DIGEST" => new DigestAuth(username, password),
                    _ => null,
                };
                if (auth is null)
                {
                    notes.Add("Authorizations Dynpost doesn't support (only Basic and Digest)", $"{Label(element)}: {mechanism}");
                    continue;
                }

                entries.Add((url.Trim(), auth));
            }

            var universal = entries is [{ Url: "" } only] ? only.Auth : null;
            return new AuthManager(entries, universal);
        }

        public RequestAuth? Match(string url) =>
            Entries.FirstOrDefault(e => e.Url.Length == 0 || url.StartsWith(e.Url, StringComparison.OrdinalIgnoreCase)).Auth;
    }

    /// <summary>What a folder-like element is: the test plan, a thread group or a controller.</summary>
    private enum LevelKind
    {
        Plan,
        ThreadGroup,
        Controller,
    }

    /// <summary>How long a run plan runs when its thread group loops forever.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(10);

    /// <summary>Timers, throughput targets, assertions and CSV data of a thread group or the whole test plan.</summary>
    private sealed class LoadSettings
    {
        public List<(TimeSpan Min, TimeSpan Max)> Timers { get; } = [];

        /// <summary>Request rates per second; <c>PerThread</c> ones are multiplied by the threads.</summary>
        public List<(double PerSecond, bool PerThread)> Rates { get; } = [];

        public List<RunAssertion?> Assertions { get; } = [];

        public ImportedDataFile? Data { get; set; }
    }

    /// <param name="Top">The thread group's child the request is in (itself, or a controller), for Throughput Controller weights.</param>
    private sealed record PlanRequest(HttpRequestDefinition Request, bool Enabled, XElement? Top, RunAssertion? Assertion);

    /// <summary>A thread group's run plan as it is read.</summary>
    private sealed class PlanBuilder(string name, RunLoad load)
    {
        public string Name { get; } = name;

        public RunLoad Load { get; } = load;

        public RequestOrder Order { get; set; }

        public LoadSettings Settings { get; } = new();

        public List<PlanRequest> Requests { get; } = [];

        public XElement? CurrentTop { get; set; }
    }

    private sealed record Argument(string Name, string Value, bool Encode);

    private sealed record FileArgument(string Name, string Path, string MimeType);

    /// <summary>The HTTP fields of a sampler or of HTTP Request Defaults.</summary>
    private sealed record HttpFields(
        string Protocol,
        string Domain,
        string Port,
        string Path,
        string Method,
        IReadOnlyList<Argument> Arguments,
        IReadOnlyList<FileArgument> Files,
        bool RawBody,
        bool Multipart,
        string ConnectTimeout,
        string ResponseTimeout,
        bool? FollowRedirects)
    {
        public static HttpFields Empty { get; } = new("", "", "", "", "", [], [], false, false, "", "", null);

        public static HttpFields Read(XElement element)
        {
            var arguments = Items(Elem(element, "HTTPsampler.Arguments"), "Arguments.arguments")
                .Select(a => new Argument(
                    Str(a, "Argument.name") ?? string.Empty,
                    Str(a, "Argument.value") ?? string.Empty,
                    Bool(a, "HTTPArgument.always_encode") == true))
                .ToList();
            var files = Items(Elem(element, "HTTPsampler.Files"), "HTTPFileArgs.files")
                .Select(f => new FileArgument(
                    Str(f, "File.paramname") ?? string.Empty,
                    Str(f, "File.path") ?? f.Attribute("name")?.Value ?? string.Empty,
                    Str(f, "File.mimetype") ?? string.Empty))
                .Where(f => f.Path.Length > 0)
                .ToList();

            // Redirects are followed unless both ways of following them are off.
            var follow = Bool(element, "HTTPSampler.follow_redirects");
            var auto = Bool(element, "HTTPSampler.auto_redirects");
            bool? followRedirects = follow is null && auto is null ? null : follow == true || auto == true ? null : false;

            return new HttpFields(
                (Str(element, "HTTPSampler.protocol") ?? string.Empty).Trim().ToLowerInvariant(),
                (Str(element, "HTTPSampler.domain") ?? string.Empty).Trim(),
                (Str(element, "HTTPSampler.port") ?? string.Empty).Trim(),
                (Str(element, "HTTPSampler.path") ?? string.Empty).Trim(),
                (Str(element, "HTTPSampler.method") ?? string.Empty).Trim(),
                arguments,
                files,
                Bool(element, "HTTPSampler.postBodyRaw") == true,
                Bool(element, "HTTPSampler.DO_MULTIPART_POST") == true,
                (Str(element, "HTTPSampler.connect_timeout") ?? string.Empty).Trim(),
                (Str(element, "HTTPSampler.response_timeout") ?? string.Empty).Trim(),
                followRedirects);
        }

        /// <summary>These fields, with those they leave empty taken from <paramref name="outer"/>.</summary>
        public HttpFields Merge(HttpFields outer) => this with
        {
            Protocol = Protocol.Length > 0 ? Protocol : outer.Protocol,
            Domain = Domain.Length > 0 ? Domain : outer.Domain,
            Port = Port.Length > 0 ? Port : outer.Port,
            Path = Path.Length > 0 ? Path : outer.Path,
            Arguments = [.. Arguments, .. outer.Arguments.Where(o => Arguments.All(a => a.Name != o.Name))],
            ConnectTimeout = ConnectTimeout.Length > 0 ? ConnectTimeout : outer.ConnectTimeout,
            ResponseTimeout = ResponseTimeout.Length > 0 ? ResponseTimeout : outer.ResponseTimeout,
        };

        /// <summary><c>https://host:8443</c>: the protocol (http by default, https for port 443), the host and a port that isn't the default.</summary>
        public string Origin()
        {
            var protocol = Protocol.Length > 0 ? Protocol : Port == "443" ? "https" : "http";
            var defaultPort = protocol == "https" ? "443" : "80";
            return Port.Length == 0 || Port == defaultPort ? $"{protocol}://{Domain}" : $"{protocol}://{Domain}:{Port}";
        }

        /// <summary>
        /// The time a whole exchange may take: the connect and response timeouts added up; <c>null</c>
        /// when there is no response timeout (Dynpost's timeout covers the whole response).
        /// </summary>
        public TimeSpan? Timeout()
        {
            if (!Milliseconds(ResponseTimeout, out var response) || response <= 0)
            {
                return null;
            }

            var connect = Milliseconds(ConnectTimeout, out var value) && value > 0 ? value : 0;
            var total = TimeSpan.FromMilliseconds(response + connect);
            return total > RequestSettings.MaxTimeout ? RequestSettings.MaxTimeout : total;
        }

        private static bool Milliseconds(string text, out long value) =>
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private enum ElementKind
    {
        Sampler,
        ThreadGroup,
        Controller,
        LogicController,
        HttpDefaults,
        Headers,
        Auth,
        JsonExtractor,
        Variables,
        Ignored,
        Script,
        OtherSampler,
        Extractor,
        LoadElement,
        Timer,
        ThroughputTimer,
        Assertion,
        CsvData,
        Listener,
        Unknown,
    }

    private static ElementKind Kind(XElement element)
    {
        var tag = element.Name.LocalName;
        var gui = element.Attribute("guiclass")?.Value ?? string.Empty;
        if (tag.Contains("JSR223", StringComparison.Ordinal) || tag.Contains("BeanShell", StringComparison.Ordinal) || tag.Contains("BSF", StringComparison.Ordinal))
        {
            return ElementKind.Script;
        }

        return tag switch
        {
            "HTTPSamplerProxy" or "HTTPSampler" or "HTTPSampler2" => ElementKind.Sampler,
            "GenericController" or "TransactionController" => ElementKind.Controller,
            "ModuleController" or "IncludeController" => ElementKind.Unknown,
            "ConfigTestElement" when gui == "HttpDefaultsGui" => ElementKind.HttpDefaults,
            "HeaderManager" => ElementKind.Headers,
            "AuthManager" => ElementKind.Auth,
            "JSONPostProcessor" => ElementKind.JsonExtractor,
            "Arguments" => ElementKind.Variables,

            // Dynpost keeps cookies and connections itself.
            "CookieManager" or "CacheManager" or "DNSCacheManager" => ElementKind.Ignored,
            "CSVDataSet" => ElementKind.CsvData,
            "ConstantTimer" or "UniformRandomTimer" or "GaussianRandomTimer" => ElementKind.Timer,
            "ConstantThroughputTimer" or "PreciseThroughputTimer" => ElementKind.ThroughputTimer,
            "ResponseAssertion" or "DurationAssertion" => ElementKind.Assertion,
            "RegexExtractor" or "BoundaryExtractor" or "XPathExtractor" or "XPath2Extractor" or "HtmlExtractor" or "JMESPathExtractor" => ElementKind.Extractor,
            _ when tag.Contains("ThreadGroup", StringComparison.Ordinal) => ElementKind.ThreadGroup,
            _ when tag.EndsWith("Controller", StringComparison.Ordinal) || tag is "InterleaveControl" or "RunTime" => ElementKind.LogicController,
            _ when tag.EndsWith("Timer", StringComparison.Ordinal) || tag.EndsWith("Assertion", StringComparison.Ordinal) => ElementKind.LoadElement,
            _ when tag.Contains("ResultCollector", StringComparison.Ordinal) || tag.EndsWith("Listener", StringComparison.Ordinal) || tag == "Summariser" => ElementKind.Listener,
            _ when tag.Contains("Sampler", StringComparison.Ordinal) => ElementKind.OtherSampler,
            _ => ElementKind.Unknown,
        };
    }

    /// <summary>The elements of a <c>hashTree</c>, each with the <c>hashTree</c> of its children.</summary>
    private static IEnumerable<(XElement Element, XElement? Tree)> Children(XElement? tree)
    {
        if (tree is null)
        {
            yield break;
        }

        // The document element holds the plan's hashTree; a hashTree holds element/hashTree pairs.
        var elements = tree.Name.LocalName == "jmeterTestPlan"
            ? tree.Elements("hashTree").Take(1).SelectMany(t => t.Elements()).ToList()
            : tree.Elements().ToList();
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i].Name.LocalName == "hashTree")
            {
                continue;
            }

            var next = i + 1 < elements.Count && elements[i + 1].Name.LocalName == "hashTree" ? elements[i + 1] : null;
            yield return (elements[i], next);
        }
    }

    private static bool Enabled(XElement element) => element.Attribute("enabled")?.Value != "false";

    private static string TestName(XElement element)
    {
        var name = element.Attribute("testname")?.Value.Trim();
        return string.IsNullOrEmpty(name) ? element.Name.LocalName : name;
    }

    private static string Comments(XElement element) => (Str(element, "TestPlan.comments") ?? string.Empty).Trim();

    /// <summary><c>'Login' (JSR223PostProcessor)</c>: an element's name and type, for warnings.</summary>
    private static string Label(XElement element) => $"'{TestName(element)}' ({element.Name.LocalName})";

    private static XElement? Prop(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Prop", StringComparison.Ordinal) && e.Attribute("name")?.Value == name);

    private static string? Str(XElement? element, string name) => Prop(element, name) is { Name.LocalName: not ("elementProp" or "collectionProp") } prop ? prop.Value : null;

    /// <summary>A property's text, also for <c>doubleProp</c>/<c>FloatProperty</c>, which name it in a child element.</summary>
    private static string? Value(XElement? element, string name) =>
        Str(element, name)
        ?? element?.Elements().FirstOrDefault(e => e.Element("name")?.Value == name)?.Element("value")?.Value;

    private static bool? Bool(XElement element, string name) =>
        Str(element, name)?.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

    private static XElement? Elem(XElement? element, string name) => Prop(element, name) is { Name.LocalName: "elementProp" } prop ? prop : null;

    /// <summary>The <c>elementProp</c> items of a collection, such as a header manager's headers.</summary>
    private static IEnumerable<XElement> Items(XElement? element, string collection) =>
        Prop(element, collection) is { Name.LocalName: "collectionProp" } prop
            ? prop.Elements().Where(e => e.Name.LocalName == "elementProp")
            : [];

    /// <summary>Warnings grouped by reason, so a plan with many skipped elements reads as a few lines.</summary>
    private sealed class Notes
    {
        private const int MaxItems = 5;
        private readonly Dictionary<string, List<string>> _items = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];

        public void Add(string reason, string item)
        {
            if (!_items.TryGetValue(reason, out var items))
            {
                items = [];
                _items[reason] = items;
                _order.Add(reason);
            }

            if (!items.Contains(item))
            {
                items.Add(item);
            }
        }

        public IEnumerable<string> Lines() =>
            _order.Select(reason =>
            {
                var items = _items[reason];
                var shown = string.Join(", ", items.Take(MaxItems));
                return items.Count > MaxItems ? $"{reason}: {shown} and {items.Count - MaxItems} more." : $"{reason}: {shown}.";
            });
    }

    /// <summary>
    /// Rewrites JMeter's <c>${…}</c> references as Dynpost <c>{{…}}</c> references: variables
    /// keep their name; <c>__UUID</c>, <c>__Random</c>, <c>__time</c> and <c>__P</c> /
    /// <c>__property</c> become built-in variables or plain variables; other functions stay as text.
    /// </summary>
    private sealed class VariableRewriter(Notes notes, IReadOnlyList<KeyValueEntry> planVariables)
    {
        public List<KeyValueEntry> PropertyDefaults { get; } = [];

        public List<string> PropertiesWithoutDefault { get; } = [];

        public string Rewrite(string text, string owner)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("${", StringComparison.Ordinal))
            {
                return text;
            }

            var result = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                var start = text.IndexOf("${", i, StringComparison.Ordinal);
                if (start < 0)
                {
                    result.Append(text, i, text.Length - i);
                    break;
                }

                var end = Closing(text, start + 2);
                if (end < 0)
                {
                    result.Append(text, i, text.Length - i);
                    break;
                }

                result.Append(text, i, start - i);
                var inner = text[(start + 2)..end];
                result.Append(Reference(inner, owner) ?? text[start..(end + 1)]);
                i = end + 1;
            }

            return result.ToString();
        }

        /// <summary>The index of the <c>}</c> that closes a reference starting at <paramref name="from"/>, skipping nested ones.</summary>
        private static int Closing(string text, int from)
        {
            var depth = 0;
            for (var i = from; i < text.Length; i++)
            {
                if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
                {
                    depth++;
                    i++;
                }
                else if (text[i] == '}')
                {
                    if (depth == 0)
                    {
                        return i;
                    }

                    depth--;
                }
            }

            return -1;
        }

        /// <summary>The Dynpost text for <c>${inner}</c>, or <c>null</c> to keep it as written.</summary>
        private string? Reference(string inner, string owner)
        {
            var trimmed = inner.Trim();
            if (!trimmed.StartsWith("__", StringComparison.Ordinal))
            {
                if (VariableTemplate.IsValidName(trimmed))
                {
                    return "{{" + trimmed + "}}";
                }

                notes.Add("Variables whose names Dynpost can't use, kept as text", $"${{{inner}}} in {owner}");
                return null;
            }

            var open = trimmed.IndexOf('(');
            var function = open < 0 ? trimmed : trimmed[..open];
            var args = open < 0 || !trimmed.EndsWith(')') ? [] : Arguments(trimmed[(open + 1)..^1]);
            switch (function)
            {
                case "__UUID":
                    return "{{$uuid}}";
                case "__Random" when args.Count >= 2
                    && long.TryParse(args[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var min)
                    && long.TryParse(args[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var max):
                    if (args.Count > 2 && args[2].Trim().Length > 0)
                    {
                        notes.Add("__Random doesn't store its value in a variable in Dynpost", owner);
                    }

                    return $"{{{{$randomInt {min} {max}}}}}";
                case "__time" when args.Count == 0 || args[0].Trim().Length == 0:
                    notes.Add("${__time()} became {{$timestamp}}000: milliseconds, but only to the second", owner);
                    return "{{$timestamp}}000";
                case "__time" when args[0].Trim() == "/1000":
                    return "{{$timestamp}}";
                case "__P" or "__property" when args.Count >= 1 && VariableTemplate.IsValidName(args[0].Trim()):
                    var name = args[0].Trim();
                    var fallback = function == "__P" ? (args.Count > 1 ? args[1] : null) : (args.Count > 2 ? args[2] : null);
                    if (fallback is not null && fallback.Length > 0)
                    {
                        if (PropertyDefaults.All(p => p.Key != name) && planVariables.All(v => v.Key != name))
                        {
                            PropertyDefaults.Add(new KeyValueEntry(name, Rewrite(fallback, owner)));
                        }
                    }
                    else if (!PropertiesWithoutDefault.Contains(name))
                    {
                        PropertiesWithoutDefault.Add(name);
                    }

                    return "{{" + name + "}}";
                default:
                    notes.Add("JMeter functions Dynpost doesn't have, kept as text", $"{function} in {owner}");
                    return null;
            }
        }

        /// <summary>A function's arguments: split on commas outside nested references; <c>\,</c> is a comma.</summary>
        private static List<string> Arguments(string text)
        {
            var args = new List<string>();
            var current = new StringBuilder();
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length && text[i + 1] == ',')
                {
                    current.Append(',');
                    i++;
                }
                else if (c == '$' && i + 1 < text.Length && text[i + 1] == '{')
                {
                    depth++;
                    current.Append("${");
                    i++;
                }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    current.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            args.Add(current.ToString());
            return args;
        }
    }
}
