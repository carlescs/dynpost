using System.Diagnostics;

namespace Missive.Infrastructure.Http;

/// <summary>
/// Connection phases of the connections one send opens: DNS lookups, TCP connects and TLS handshakes,
/// summed when a send opens several connections (e.g. a redirect to another host).
/// </summary>
internal sealed class ConnectionTimings
{
    private readonly object _lock = new();
    private TimeSpan? _dns;
    private TimeSpan? _connect;
    private TimeSpan? _tls;
    private long _connectedAt;
    private int _opened;

    public TimeSpan? Dns
    {
        get { lock (_lock) { return _dns; } }
    }

    public TimeSpan? Connect
    {
        get { lock (_lock) { return _connect; } }
    }

    public TimeSpan? Tls
    {
        get { lock (_lock) { return _tls; } }
    }

    /// <summary>The time spent on all connection phases so far.</summary>
    public TimeSpan Setup
    {
        get
        {
            lock (_lock)
            {
                return (_dns ?? TimeSpan.Zero) + (_connect ?? TimeSpan.Zero) + (_tls ?? TimeSpan.Zero);
            }
        }
    }

    /// <summary>How many connections have been opened for the send so far, including failed attempts.</summary>
    public int Opened
    {
        get { lock (_lock) { return _opened; } }
    }

    /// <summary>Records that a new connection is being opened.</summary>
    public void Opening()
    {
        lock (_lock)
        {
            _opened++;
        }
    }

    public void AddDns(TimeSpan duration)
    {
        lock (_lock)
        {
            _dns = (_dns ?? TimeSpan.Zero) + duration;
        }
    }

    /// <summary>Records a TCP connect that has just finished.</summary>
    public void Connected(TimeSpan duration)
    {
        lock (_lock)
        {
            _connect = (_connect ?? TimeSpan.Zero) + duration;
            _connectedAt = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Records the end of the TLS handshake on the connection opened last.</summary>
    public void HandshakeDone()
    {
        lock (_lock)
        {
            if (_connectedAt == 0)
            {
                return;
            }

            _tls = (_tls ?? TimeSpan.Zero) + Stopwatch.GetElapsedTime(_connectedAt);
            _connectedAt = 0;
        }
    }
}
