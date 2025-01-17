﻿using System;
using System.Net.Mail;
using System.Reactive.Linq;
using System.Security;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.Downloaders.Interfaces;
using Wabbajack;

namespace Wabbajack
{
    public class CredentialsLoginVM : ViewModel
    {
        [Reactive]
        public string Username { get; set; }

        [Reactive]
        public string MFAKey { get; set; }

        
        [Reactive]
        public object ReturnMessage { get; set; }

        [Reactive]
        public bool LoggingIn { get; private set; }

        private readonly ObservableAsPropertyHelper<bool> _loginEnabled;
        public bool LoginEnabled => _loginEnabled.Value;

        private readonly ObservableAsPropertyHelper<bool> _mfaVisible;
        public bool MFAVisible => _mfaVisible.Value;

        private readonly object _downloader;

        public CredentialsLoginVM(INeedsLoginCredentials downloader)
        {
            _downloader = downloader;

            _loginEnabled = this.WhenAny(x => x.Username)
                .Select(IsValidAddress)
                .CombineLatest(
                    this.WhenAny(x => x.LoggingIn),
                    (valid, loggingIn) =>
                    {
                        return valid && !loggingIn;
                    })
                .ToGuiProperty(this,
                    nameof(LoginEnabled));

            /*
            _mfaVisible = this.WhenAny(x => x.ReturnMessage)
                .Select(x => x.ReturnCode == LoginReturnCode.NeedsMFA)
                .ToGuiProperty(this, nameof(MFAVisible));
                */
        }

        public async Task Login(SecureString password)
        {
            /*
            try
            {
                LoggingIn = true;

                if (password == null || password.Length == 0)
                {
                    ReturnMessage = new LoginReturnMessage("You need to input a password!", LoginReturnCode.BadInput);
                    return;
                }

                ReturnMessage = await _downloader.LoginWithCredentials(Username, password, string.IsNullOrWhiteSpace(MFAKey) ? null : MFAKey);
                password.Clear();
            }
            catch (Exception e)
            {
                Utils.Error(e, "Exception while trying to login");
                ReturnMessage = new LoginReturnMessage($"Unhandled exception: {e.Message}", LoginReturnCode.InternalError);
            }
            finally
            {
                LoggingIn = false;
            }
            */
        }

        private static bool IsValidAddress(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            try
            {
                var _ = new MailAddress(s);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
