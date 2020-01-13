using System;

using Xamarin.Forms;

namespace GWallet.Frontend.XF.CS
{

    public static class Initialization
    {
        internal static Page LandingPage()
        {
            Page landingPage = new ReceivePage();

            var navPage = new NavigationPage(landingPage);
            NavigationPage.SetHasNavigationBar(landingPage, false);
            return navPage;
        }
    }
}
