
using System;
using System.ComponentModel;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace GWallet.Frontend.XF.CS
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(false)]
    public partial class ReceivePage : ContentPage
    {
        public ReceivePage()
        {
            InitializeComponent();
        }

        private void OnSendPaymentClicked(object sender, EventArgs args)
        {
            var sendPage = new SendPage();

            NavigationPage.SetHasNavigationBar(sendPage, false);
            var navSendPage = new NavigationPage(sendPage);
            NavigationPage.SetHasNavigationBar(navSendPage, false);

            this.Navigation.PushAsync(navSendPage);
        }
    }
}