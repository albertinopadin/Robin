﻿using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Robin
{
    internal class RobinUpdater
    {
        public static void InstallUpdateSyncWithInfo()
        {
            UpdateCheckInfo info = null;

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                Console.WriteLine("[InstallUpdateSyncWithInfo] App is Network Deployed");

                ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

                Console.WriteLine("[InstallUpdateSyncWithInfo] Current version: " + ad.CurrentVersion);

                try
                {
                    info = ad.CheckForDetailedUpdate();
                }
                catch (DeploymentDownloadException dde)
                {
                    MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                        "\n\nThe new version of the application cannot be downloaded at this time. " +
                        $"\n\nPlease check your network connection, or try again later. Error: {dde.Message}");
                    return;
                }
                catch (InvalidDeploymentException ide)
                {
                    MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                        "\n\nCannot check for a new version of the application. The ClickOnce deployment is corrupt. " +
                        $"Please redeploy the application and try again. Error: {ide.Message}");
                    return;
                }
                catch (InvalidOperationException ioe)
                {
                    MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                        "This application cannot be updated. It is likely not a ClickOnce application. " +
                        $"Error: {ioe.Message}");
                    return;
                }

                if (info.UpdateAvailable)
                {
                    Boolean doUpdate = true;

                    if (!info.IsUpdateRequired)
                    {
                        DialogResult dr = MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                            "An update is available. Would you like to update the application now?",
                            "Update Available", MessageBoxButtons.OKCancel);
                        if (!(DialogResult.OK == dr))
                        {
                            doUpdate = false;
                        }
                    }
                    else
                    {
                        // Display a message that the app MUST reboot. Display the minimum required version.
                        MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                            "This application has detected a mandatory update from your current " +
                            $"version to version {info.MinimumRequiredVersion.ToString()}. " +
                            "The application will now install the update and restart.",
                            "Update Available", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }

                    if (doUpdate)
                    {
                        try
                        {
                            ad.Update();
                            MessageBox.Show("The application has been upgraded, and will now restart.");
                            System.Windows.Forms.Application.Restart();
                        }
                        catch (DeploymentDownloadException dde)
                        {
                            MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}" +
                                "Cannot install the latest version of the application. " +
                                "\n\nPlease check your network connection, or try again later. Error: " + dde);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("doUpdate is false");
                    }
                }
                else
                {
                    MessageBox.Show($"Application Deployment Current Version: {ad.CurrentVersion}\nNo updates available.");
                }
            }
            else
            {
                Console.WriteLine("[InstallUpdateSyncWithInfo] App is NOT Network Deployed");
                MessageBox.Show("App is NOT Network Deployed");
            }
        }
    }
}
