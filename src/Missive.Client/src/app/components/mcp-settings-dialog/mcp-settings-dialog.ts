import { DialogRef } from '@angular/cdk/dialog';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCopy, faEye, faEyeSlash, faXmark } from '@fortawesome/free-solid-svg-icons';
import { copyText } from '../../models/clipboard';
import { McpStatusDto } from '../../models/messages';
import { Bridge, BridgeError } from '../../services/bridge';
import { Dialogs } from '../../services/dialogs';

/** The ports the host accepts for the MCP server. */
export const MIN_MCP_PORT = 1024;
export const MAX_MCP_PORT = 65535;

/**
 * Settings of the MCP server that lets AI assistants (Claude Code, Claude Desktop and other MCP
 * clients) control Missive: turning it on, its port and token, and how to connect a client. The
 * settings are per user (the host's settings file). Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-mcp-settings-dialog',
  imports: [FaIconComponent],
  templateUrl: './mcp-settings-dialog.html',
})
export class McpSettingsDialog implements OnInit {
  private readonly bridge = inject(Bridge);
  private readonly dialogs = inject(Dialogs);

  protected readonly ref = inject(DialogRef);
  protected readonly status = signal<McpStatusDto | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly port = signal(0);
  protected readonly showToken = signal(false);
  /** What was copied last, to confirm it next to its button. */
  protected readonly copied = signal<string | null>(null);
  protected readonly portChanged = computed(() => this.port() !== this.status()?.port);

  /** The command that adds the server to Claude Code. */
  protected readonly claudeCommand = computed(() => {
    const status = this.status();
    return status?.token
      ? `claude mcp add --transport http missive ${status.url} --header "Authorization: Bearer ${status.token}"`
      : '';
  });

  /** The server's entry in the JSON configuration other MCP clients use. */
  protected readonly jsonConfig = computed(() => {
    const status = this.status();
    if (!status?.token) return '';
    const config = {
      mcpServers: {
        missive: {
          type: 'http',
          url: status.url,
          headers: { Authorization: `Bearer ${status.token}` },
        },
      },
    };
    return JSON.stringify(config, null, 2);
  });

  protected readonly faXmark = faXmark;
  protected readonly faCopy = faCopy;
  protected readonly faEye = faEye;
  protected readonly faEyeSlash = faEyeSlash;

  async ngOnInit(): Promise<void> {
    await this.run(() => this.bridge.send('mcp.status'));
  }

  protected async setEnabled(enabled: boolean): Promise<void> {
    await this.run(() => this.bridge.send('mcp.set', { enabled }));
  }

  protected async applyPort(): Promise<void> {
    const port = this.port();
    if (!Number.isInteger(port) || port < MIN_MCP_PORT || port > MAX_MCP_PORT) {
      this.error.set(`Choose a port between ${MIN_MCP_PORT} and ${MAX_MCP_PORT}.`);
      return;
    }
    await this.run(() => this.bridge.send('mcp.set', { enabled: !!this.status()?.enabled, port }));
  }

  protected async regenerateToken(): Promise<void> {
    const confirmed = await this.dialogs.confirm(
      'AI assistants set up with the current token will no longer be able to connect until you give them the new one.',
      { title: 'Replace the token?', confirmLabel: 'Replace', danger: true },
    );
    if (!confirmed) return;
    await this.run(() => this.bridge.send('mcp.regenerateToken'));
  }

  protected async copy(what: string, text: string): Promise<void> {
    if (await copyText(text)) this.copied.set(what);
  }

  private async run(action: () => Promise<McpStatusDto>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const status = await action();
      this.status.set(status);
      this.port.set(status.port);
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    } finally {
      this.busy.set(false);
    }
  }
}
