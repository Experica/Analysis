/*
VLAUIController.cs is part of the VLAB project.
Copyright (c) 2016 Li Alex Zhang and Contributors

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included 
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Diagnostics;
using System.Collections;
using VLab;

namespace VLabAnalysis
{
    public class VLAUIController : MonoBehaviour
    {
        public InputField serveraddress;
        public Toggle clientconnect, autoconn;
        public Text autoconntext;
        public VLANetManager netmanager;
        public VLAApplicationManager appmanager;
        public VLAnalysisManager alsmanager;
        public VLControlManager ctrlmanager;
        public ControlPanel controlpanel;
        public SignalPanel signalpanel;
        public Text version;

        bool isautoconn, isconnect;
        int autoconncountdown;
        float lastautoconntime;

        public void OnToggleClientConnect(bool isconn)
        {
            if (isconn)
            {
                netmanager.networkAddress = serveraddress.text;
                netmanager.StartClient();
            }
            else
            {
                netmanager.StopClient();
                OnClientDisconnect();
            }
        }

        public void OnServerAddressEndEdit(string v)
        {
            appmanager.config.ServerAddress = v;
        }

        public void OnToggleAutoConnect(bool ison)
        {
            appmanager.config.AutoConnect = ison;
            ResetAutoConnect();
        }

        public void ResetAutoConnect()
        {
            autoconncountdown = appmanager.config.AutoConnectTimeOut;
            isautoconn = appmanager.config.AutoConnect;
            if (!isautoconn)
            {
                autoconntext.text = "Auto Connect OFF";
            }
            autoconn.isOn = isautoconn;
        }

        void Start()
        {
            serveraddress.text = appmanager.config.ServerAddress;
            ResetAutoConnect();
        }

        void Update()
        {
            if (!isconnect)
            {
                if (isautoconn)
                {
                    if (Time.unscaledTime - lastautoconntime >= 1)
                    {
                        autoconncountdown--;
                        if (autoconncountdown > 0)
                        {
                            lastautoconntime = Time.unscaledTime;
                            autoconntext.text = "Auto Connect " + autoconncountdown + "s";
                        }
                        else
                        {
                            clientconnect.isOn = true;
                            clientconnect.onValueChanged.Invoke(true);
                            autoconntext.text = "Connecting ...";
                            isautoconn = false;
                        }
                    }
                }
            }
        }

        public void OnClientConnect()
        {
            isconnect = true;
            autoconntext.text = "Connected";
        }

        public void OnAnalysisManagerSpwaned()
        {
            autoconntext.text = "AnalysisManager Online";
            if (alsmanager.als == null)
            {
                var das = appmanager.config.AnalysisSystem;
                var cdpa = appmanager.config.ClearDataPerAnalysis;
                var rapc = appmanager.config.RetainAnalysisPerClear;
                var asr = appmanager.config.AnalysisSleepResolution;
                alsmanager.als = das.GetAnalysisSystem(cdpa, rapc, asr);
            }

            if (appmanager.config.SearchSignalWhenConnect)
            {
                SearchSignal();
            }
        }

        public void OnControlManagerSpwaned()
        {
            if (alsmanager != null)
            {
                autoconntext.text = "Ready";
            }
            else
            {
                autoconntext.text = "ControlManager Online";
            }
        }

        public void OnClientDisconnect()
        {
            if (alsmanager != null)
            {
                if (alsmanager.als != null)
                {
                    alsmanager.als.Dispose();
                    alsmanager.als = null;
                    signalpanel.UpdateSignal(false);
                }
                Destroy(alsmanager.gameObject);
                alsmanager = null;
                Destroy(ctrlmanager.gameObject);
                ctrlmanager = null;
            }
            isconnect = false;
            clientconnect.isOn = false;
            ResetAutoConnect();
        }

        public void SearchSignal()
        {
            if (alsmanager != null && alsmanager.als != null)
            {
                var ss = controlpanel.signalsourcedropdown.captionText.text;
                var s = ss == "All" ? VLAExtention.SearchSignal() : ss.Convert<SignalSource>().SearchSignal();
                var sr = false;
                if (s != null)
                {
                    alsmanager.als.Signal = s;
                    sr = true;
                }
                signalpanel.UpdateSignal(sr);
            }
        }

        public void UpdateVisualizationDone(int done)
        {
            controlpanel.visualizationdone.text = done.ToString();
        }

        public void UpdateAnalysisEventIndex(int aeidx)
        {
            controlpanel.analysiseventindex.text = aeidx.ToString();
        }

        public void UpdateAnalysisDone(int done)
        {
            controlpanel.analysisdone.text = done.ToString();
        }

        public void UpdateSystemInformation()
        {
            version.text = $"Version {Application.version}\nUnity {Application.unityVersion}";
        }
    }
}