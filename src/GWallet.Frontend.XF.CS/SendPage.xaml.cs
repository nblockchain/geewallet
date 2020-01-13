using System;
using System.Threading.Tasks;
using System.ComponentModel;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace GWallet.Frontend.XF.CS
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(false)]
    public partial class SendPage : ContentPage
    {
        public SendPage()
        {
            InitializeComponent();
        }

        private Task SomeFunc()
        {
            return Device.InvokeOnMainThreadAsync(() =>
                this.DisplayAlert("Alert", "BAZ", "OK")
            );
        }

        private Task ToggleInputWidgetsEnabledOrDisabled(bool enabled)
        {
            return Device.InvokeOnMainThreadAsync(() => {
                if (enabled && (!button.IsEnabled))
                {
                    button.Text = "reenabled";
                }
                button.IsEnabled = enabled;
            });
        }

        private void OnButtonClicked(object sender, EventArgs args)
        {
            var task = this.ToggleInputWidgetsEnabledOrDisabled(false);
            task.ContinueWith(t1 => {
                SomeFunc().ContinueWith(t2 =>
                {
                    this.ToggleInputWidgetsEnabledOrDisabled(true);
                });
            });
        }
    }
}