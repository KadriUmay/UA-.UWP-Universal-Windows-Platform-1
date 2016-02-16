using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Sample.Controls;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using Windows.UI.Text;
using Windows.UI;
using System.Threading;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Opc.Ua.SampleClient
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class ClientPage : Page
    {
        #region Private Fields
        private Session m_session;
        private ApplicationInstance m_application;
        private Opc.Ua.Server.StandardServer m_server;
        private ConfiguredEndpointCollection m_endpoints;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_context;
        private ClientPage m_masterPage;
        private List<ClientPage> m_pages;
        #endregion

        public ClientPage()
        {
            InitializeComponent();
        }

        public ClientPage(
           ServiceMessageContext context,
           ApplicationInstance application,
           ClientPage masterPage,
           ApplicationConfiguration configuration)
        {
            InitializeComponent();

            if (!configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
        
            m_masterPage = masterPage;
            m_context = context;
            m_application = application;
            m_server = application.Server as Opc.Ua.Server.StandardServer;

            if (m_masterPage == null)
            {
                m_pages = new List<ClientPage>();
            }

            m_configuration = configuration;
            
            SessionsCTRL.Configuration = configuration;
            SessionsCTRL.MessageContext = context;

            // get list of cached endpoints.
            m_endpoints = m_configuration.LoadCachedEndpoints(true);
            m_endpoints.DiscoveryUrls = configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            
            // hook up endpoint selector
            EndpointSelectorCTRL.Initialize(m_endpoints, m_configuration);
            EndpointSelectorCTRL.ConnectEndpoint += EndpointSelectorCTRL_ConnectEndpoint;
            EndpointSelectorCTRL.EndpointsChanged += EndpointSelectorCTRL_OnChange;

            // exception dialog
            GuiUtils.ExceptionMessageDlg += ExceptionMessageDlg;

            // initialize control state.
            Disconnect();
        }

        void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            ManualResetEvent ev = new ManualResetEvent(false);
            Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
                {
                    await GuiUtils.HandleCertificateValidationError(this, validator, e);
                    ev.Set();
                }
                ).AsTask().Wait();
            ev.WaitOne();
        }

        public void OpenPage()
        {
            if (m_masterPage == null)
            {
                ClientPage page = new ClientPage(m_context, m_application, this, m_configuration);
                m_pages.Add(page);
                page.Unloaded += Window_PageClosing;
            }
            else
            {
                m_masterPage.OpenPage();
            }
        }

        async void Window_PageClosing(object sender, RoutedEventArgs e)
        {
            if (m_masterPage == null && m_pages.Count > 0)
            {
                MessageDlg dialog = new MessageDlg("Close all sessions?", MessageDlgButton.Yes, MessageDlgButton.No);
                MessageDlgButton result = await dialog.ShowAsync();
                if (result != MessageDlgButton.Yes)
                {
                    return;
                }
            }

            Disconnect();

            for (int ii = 0; ii < m_pages.Count; ii++)
            {
                if (Object.ReferenceEquals(m_pages[ii], sender))
                {
                    m_pages.RemoveAt(ii);
                    break;
                }
            }
        }

        /// <summary>
        /// Disconnects from a server.
        /// </summary>
        public void Disconnect()
        {
            BrowseCTRL.SetView(null, BrowseViewType.Objects, null);
            ServerUrlTB.Text = "None";

            if (m_session != null)
            {
                m_session.Close();
                m_session = null;
            }
        }

        /// <summary>
        /// Provides a user defined method.
        /// </summary>
        protected virtual async void DoTest(Session session)
        {
            MessageDlg dialog = new MessageDlg("A handy place to put test code.");
            await dialog.ShowAsync();
        }

        async Task EndpointSelectorCTRL_ConnectEndpoint(object sender, ConnectEndpointEventArgs e)
        {
            try
            {
                // disable Connect while connecting button
                EndpointSelectorCTRL.IsEnabled = false;
                // Connect
                e.UpdateControl = await Connect(e.Endpoint);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
                e.UpdateControl = false;
            }
            finally
            {
                // enable Connect button
                EndpointSelectorCTRL.IsEnabled = true;
            }
        }

        private void EndpointSelectorCTRL_OnChange(object sender, EventArgs e)
        {
            try
            {
                m_endpoints.Save();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        /// <summary>
        /// Connects to a server.
        /// </summary>
        public async Task<bool> Connect(ConfiguredEndpoint endpoint)
        {
            bool result = false;
            if (endpoint == null)
            {
                return false;
            }

            // connect dialogs
            Session session = await SessionsCTRL.Connect(endpoint);

            if (session != null)
            {
                // disconnect existing session and clean up tree control
                Disconnect();

                //hook up new session
                m_session = session;
                m_session.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                BrowseCTRL.SetView(m_session, BrowseViewType.Objects, null);
                StandardClient_KeepAlive(m_session, null);
                result = true;
            }

            return result;
        }

        /// <summary>
        /// Updates the status control when a keep alive event occurs.
        /// </summary>
        async void StandardClient_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (!Dispatcher.HasThreadAccess)
            {
                await Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                {
                    StandardClient_KeepAlive( sender, e);
                });
                return;
            }

            if (sender != null && sender.Endpoint != null)
            {
                ServerUrlTB.Text = Utils.Format(
                    "{0} ({1}) {2}",
                    sender.Endpoint.EndpointUrl,
                    sender.Endpoint.SecurityMode,
                    (sender.EndpointConfiguration.UseBinaryEncoding) ? "UABinary" : "XML");
            }
            else
            {
                ServerUrlTB.Text = "None";
            }

            if (e != null && m_session != null)
            {
                SessionsCTRL.UpdateSessionNode(m_session);

                if (ServiceResult.IsGood(e.Status))
                {
                    ServerStatusTB.Text = Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}",
                        e.CurrentState,
                        e.CurrentTime.ToLocalTime(),
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount);
                    ServerStatusTB.Foreground = new SolidColorBrush(Colors.Black);
                    ServerStatusTB.FontWeight = FontWeights.Normal;
                }
                else
                {
                    ServerStatusTB.Text = String.Format(
                        "{0} {1}/{2}", e.Status,
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount);
                    ServerStatusTB.Foreground = new SolidColorBrush(Colors.Red);
                    ServerStatusTB.FontWeight = FontWeights.Bold;
                }
            }
        }

        async void ExceptionMessageDlg(string message)
        {
            await Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                async () =>
            {
                MessageDlg dialog = new MessageDlg(message);
                await dialog.ShowAsync();
            });
        }

        private void MainPage_PageClosing(object sender, RoutedEventArgs e)
        {
            try
            {
                SessionsCTRL.Close();

                if (m_masterPage == null)
                {
                    m_application.Stop();
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void DiscoverServersMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConfiguredEndpoint endpoint = new ConfiguredServerListDlg().ShowDialog(m_configuration, true);

                if (endpoint != null)
                {
                    EndpointSelectorCTRL.SelectedEndpoint = endpoint;
                    return;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void NewWindowMI_Click(object sender, EventArgs e)
        {
            try
            {
                this.OpenPage();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void Discovery_RegisterMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_server != null)
                {
                    OnRegister(null);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private async void OnRegister(object sender)
        {
            try
            {
                Opc.Ua.Server.StandardServer server = m_server;

                if (server != null)
                {
                    await server.RegisterWithDiscoveryServer();
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Could not register with the LDS");
            }
        }

        private void Task_TestMI_Click(object sender, EventArgs e)
        {
            try
            {
                DoTest(m_session);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

    }
}
