﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Wabbajack;
using Wabbajack.LoginManagers;
using Wabbajack.Messages;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Services.OSIntegrated.TokenProviders;
using Wabbajack.View_Models.Settings;

namespace Wabbajack
{
    public class SettingsVM : BackNavigatingVM
    {
        public LoginManagerVM Login { get; }
        public PerformanceSettings Performance { get; }
        public FiltersSettings Filters { get; }
        public AuthorFilesVM AuthorFile { get; }

        public ICommand OpenTerminalCommand { get; }

        public SettingsVM(ILogger<SettingsVM> logger, IServiceProvider provider)
            : base(logger)
        {
            Login = new LoginManagerVM(provider.GetRequiredService<ILogger<LoginManagerVM>>(), this, 
                provider.GetRequiredService<IEnumerable<INeedsLogin>>());
            AuthorFile = new AuthorFilesVM(provider.GetRequiredService<ILogger<AuthorFilesVM>>()!, 
                provider.GetRequiredService<WabbajackApiTokenProvider>()!, provider.GetRequiredService<Client>()!, this);
            OpenTerminalCommand = ReactiveCommand.CreateFromTask(OpenTerminal);
            BackCommand = ReactiveCommand.Create(NavigateBack.Send);
        }

        private async Task OpenTerminal()
        {
            var process = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!
            };
            Process.Start(process);
        }
    }
}
