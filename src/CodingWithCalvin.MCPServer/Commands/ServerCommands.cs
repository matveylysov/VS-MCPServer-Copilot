using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using CodingWithCalvin.MCPServer.Services;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CodingWithCalvin.MCPServer.Commands;

internal sealed class ServerCommands
{
    private const int SubmitCopilotMessageCommandId = 0x0106;

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService == null)
        {
            return;
        }

        // Start Server command
        var startCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidStartServer);
        var startCommand = new OleMenuCommand(OnStartServer, startCommandId);
        startCommand.BeforeQueryStatus += OnBeforeQueryStatusStart;
        commandService.AddCommand(startCommand);

        // Stop Server command
        var stopCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidStopServer);
        var stopCommand = new OleMenuCommand(OnStopServer, stopCommandId);
        stopCommand.BeforeQueryStatus += OnBeforeQueryStatusStop;
        commandService.AddCommand(stopCommand);

        // Restart Server command
        var restartCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidRestartServer);
        var restartCommand = new OleMenuCommand(OnRestartServer, restartCommandId);
        restartCommand.BeforeQueryStatus += OnBeforeQueryStatusStop;
        commandService.AddCommand(restartCommand);

        // Copy Server URL command
        var copyUrlCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidCopyServerUrl);
        var copyUrlCommand = new OleMenuCommand(OnCopyServerUrl, copyUrlCommandId);
        commandService.AddCommand(copyUrlCommand);

        // Show Available Tools command
        var showToolsCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidShowTools);
        var showToolsCommand = new OleMenuCommand(OnShowTools, showToolsCommandId);
        showToolsCommand.BeforeQueryStatus += OnBeforeQueryStatusStop;
        commandService.AddCommand(showToolsCommand);

        // Open GitHub Copilot pane command
        var openGitHubCopilotCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidOpenGitHubCopilot);
        var openGitHubCopilotCommand = new OleMenuCommand(OnOpenGitHubCopilot, openGitHubCopilotCommandId);
        commandService.AddCommand(openGitHubCopilotCommand);

        // Submit Copilot message command
        var submitCopilotMessageCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, SubmitCopilotMessageCommandId);
        var submitCopilotMessageCommand = new OleMenuCommand(OnSubmitCopilotMessage, submitCopilotMessageCommandId);
        commandService.AddCommand(submitCopilotMessageCommand);
    }

    private static void EnsureServicesInitialized()
    {
        MCPServerPackage.Instance?.InitializeServices();
    }

    private static void OnBeforeQueryStatusStart(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Enabled = MCPServerPackage.ServerManager == null || !MCPServerPackage.ServerManager.IsRunning;
        }
    }

    private static void OnBeforeQueryStatusStop(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Enabled = MCPServerPackage.ServerManager != null && MCPServerPackage.ServerManager.IsRunning;
        }
    }

    private static ServerStartSettings CaptureSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var settings = MCPServerPackage.Settings;

        return new ServerStartSettings
        {
            BindingAddress = settings?.BindingAddress ?? "localhost",
            Port = settings?.Port ?? 5050,
            ServerName = settings?.ServerName ?? "Visual Studio MCP",
            LogLevel = settings?.LogLevel.ToString() ?? "Information",
            LogRetentionDays = settings?.LogRetentionDays ?? 7,
            OutputPane = MCPServerPackage.OutputPaneService?.GetPane()
        };
    }

    private static void OnStartServer(object sender, EventArgs e)
    {
        // Capture everything we need on UI thread before going to background
        EnsureServicesInitialized();
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null) return;

        var startSettings = CaptureSettings();

        _ = Task.Run(async () =>
        {
            await serverManager.StartAsync(startSettings);

            // Refresh command states on UI thread after server starts
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        });
    }

    private static void OnStopServer(object sender, EventArgs e)
    {
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null) return;

        _ = Task.Run(async () =>
        {
            await serverManager.StopAsync();

            // Refresh command states on UI thread after server stops
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        });
    }

    private static void OnRestartServer(object sender, EventArgs e)
    {
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null) return;

        var startSettings = CaptureSettings();

        _ = Task.Run(async () =>
        {
            await serverManager.StopAsync();
            await serverManager.StartAsync(startSettings);

            // Refresh command states on UI thread after server restarts
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        });
    }

    private static void OnCopyServerUrl(object sender, EventArgs e)
    {
        var port = MCPServerPackage.Settings?.Port ?? 5050;
        var url = $"http://localhost:{port}/sse";
        System.Windows.Clipboard.SetText(url);
    }

    private static void OnShowTools(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (MCPServerPackage.Instance == null)
            {
                return;
            }

            if (MCPServerPackage.RpcServer == null || !MCPServerPackage.RpcServer.IsConnected)
            {
                VsShellUtilities.ShowMessageBox(
                    MCPServerPackage.Instance,
                    "Server is not connected. Start the server first.",
                    "VS MCP Server",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var tools = await MCPServerPackage.RpcServer.GetAvailableToolsAsync();
            var message = tools.Count == 0
                ? "No tools available."
                : string.Join("\n", tools.GroupBy(t => t.Category).OrderBy(g => g.Key)
                    .SelectMany(g => new[] { $"\n{g.Key.ToUpperInvariant()} TOOLS:" }
                        .Concat(g.OrderBy(t => t.Name).Select(t => $"  {t.Name}"))));

            VsShellUtilities.ShowMessageBox(
                MCPServerPackage.Instance,
                message,
                "VS MCP Server - Available Tools",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        });
    }

    //Alt+Q - add current file to Copilot chat context. It also opens the chat - !!!
    //Alt+E - View.GitHub.Copilot.Chat - open GitHub Copilot Chat pane
    //Alt+Y - View.OpenChatVindow - also opens GitHub Copilot chat
    //Alt+N - GitHub.Copilot.Chat.NwwThread - Creates new thread (chat) - should focus GitHub Copilot Chat
    //Ctrl+PgUp - GitHub.Copilot.Chat.NextThread - focuses next thread in GitHub Copilot Chat
    //Ctrl.PgDown - GitHub.Copilot.Chat.PreviousThread - focuses previous thread in GitHub Copilot Chat

    private static void OnOpenGitHubCopilot(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (MCPServerPackage.Instance == null)
            {
                return;
            }

            SendKeys.SendWait("^\\");
            SendKeys.SendWait("^{c}");
        });
    }

    private static void OnSubmitCopilotMessage(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (MCPServerPackage.Instance == null)
            {
                return;
            }

            var message = "Hello there";

            var copilotWindow = await EnsureCopilotChatLoadedOrThrowAsync();

            var askCopilotTextBox = copilotWindow.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Ask Copilot"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));

            if (askCopilotTextBox == null)
            {
                throw new InvalidOperationException("Ask Copilot textbox was not found after GitHub Copilot Chat loaded.");
            }

            if (askCopilotTextBox.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) && valuePatternObject is ValuePattern valuePattern)
            {
                valuePattern.SetValue(message);
            }
            else
            {
                askCopilotTextBox.SetFocus();
                SendKeys.SendWait(message);
            }

            var sendButton = copilotWindow.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Send"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
            if (sendButton == null)
            {
                throw new InvalidOperationException("Send button was not found after GitHub Copilot Chat loaded.");
            }

            if (sendButton.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObject) && invokePatternObject is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return;
            }
        });
    }

    private static async System.Threading.Tasks.Task<AutomationElement> EnsureCopilotChatLoadedOrThrowAsync()
    {
        const int timeoutMs = 20000;

        var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not get main Visual Studio window handle.");
        }

        var mainWindow = AutomationElement.FromHandle(mainWindowHandle);
        if (mainWindow == null)
        {
            throw new InvalidOperationException("Could not access main Visual Studio window automation element.");
        }

        var timeoutAt = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < timeoutAt)
        {
            var copilotWindow = mainWindow.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "GitHub Copilot Chat"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)));

            if (copilotWindow != null)
            {
                var askCopilotTextBox = copilotWindow.FindFirst(
                    TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.NameProperty, "Ask Copilot"),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));

                if (askCopilotTextBox != null)
                {
                    return copilotWindow;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("GitHub Copilot Chat window was not loaded within 10 seconds.");
    }

}
