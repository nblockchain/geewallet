
using Xamarin.Forms;

namespace GWallet.Frontend.XF.CS
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = Initialization.LandingPage();
        }
    }
}

