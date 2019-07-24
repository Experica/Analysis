/*
UIController.cs is part of the Experica.
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
using System.IO;

namespace Experica.Analysis
{
    public class UIController : MonoBehaviour
    {
        public AnalysisConfig config;
        readonly string configpath = "AnalysisConfig.yaml";

        public InputField serveraddress;
        public Toggle clientconnect, autoconnect;
        public Text autoconnecttext;
        public NetManager netmanager;
        public AnalysisManager alsmanager;
        public ControlManager ctrlmanager;
        public ControlPanel controlpanel;
        public SignalPanel signalpanel;
        public Text version;

        bool _isautoconnect, _isconnected;
        int _autotaskcountdown;
        float _lastautotasktime;

        void Awake()
        {
            if (File.Exists(configpath))
            {
                config = configpath.ReadYamlFile<AnalysisConfig>();
            }
            if (config == null)
            {
                config = new AnalysisConfig();
            }
        }

        void Start()
        {
            version.text = $"Version {Application.version}\nUnity {Application.unityVersion}";
            serveraddress.text = config.ServerAddress;
            ResetAutoTask();
        }

        void Update()
        {
            if (!_isconnected)
            {
                if (_isautoconnect && (Time.unscaledTime - _lastautotasktime >= 1))
                {
                    _autotaskcountdown--;
                    if (_autotaskcountdown > 0)
                    {
                        _lastautotasktime = Time.unscaledTime;
                        autoconnecttext.text = "Auto Connect " + _autotaskcountdown + "s";
                    }
                    else
                    {
                        clientconnect.isOn = true;
                        clientconnect.onValueChanged.Invoke(true);
                        autoconnecttext.text = "Connecting ...";
                        _isautoconnect = false;
                    }
                }
            }
        }

        void OnApplicationQuit()
        {
            OnToggleClientConnect(false);
            configpath.WriteYamlFile(config);
        }


        public void OnToggleClientConnect(bool isconnect)
        {
            if (isconnect)
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
            config.ServerAddress = v;
        }

        public void OnToggleAutoConnect(bool ison)
        {
            config.AutoConnect = ison;
            ResetAutoTask();
        }

        public void ResetAutoTask()
        {
            _autotaskcountdown = config.AutoTaskTimeOut;
            _isautoconnect = config.AutoConnect;
            autoconnect.isOn = _isautoconnect;
            if (!_isautoconnect)
            {
                autoconnecttext.text = "Auto Connect OFF";
            }
        }

        public void OnClientConnect()
        {
            _isconnected = true;
            autoconnecttext.text = "Connected";
        }

        public void OnAnalysisManagerSpwaned()
        {
            autoconnecttext.text = "AnalysisManager Online";
            if (alsmanager.als == null)
            {
                var ae = config.AnalysisEngine;
                var cdpa = config.ClearDataPerAnalysis;
                var rapc = config.RetainAnalysisPerClear;
                var asd = config.AnalysisSleepDuration;
                alsmanager.als = ae.GetAnalysisEngine(cdpa, rapc, asd);
                if (ctrlmanager != null && ctrlmanager.als == null)
                {
                    ctrlmanager.als = alsmanager.als;
                }
            }

            if (config.SearchSignalWhenConnect)
            {
                SearchSignal();
            }
        }

        public void OnControlManagerSpwaned()
        {
            if (alsmanager != null)
            {
                autoconnecttext.text = "Ready";
                if (ctrlmanager.als == null)
                {
                    ctrlmanager.als = alsmanager.als;
                }
            }
            else
            {
                autoconnecttext.text = "ControlManager Online";
            }
            
        }

        public void OnClientDisconnect()
        {
            if (alsmanager != null && ctrlmanager != null)
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
            _isconnected = false;
            clientconnect.isOn = false;
            ResetAutoTask();
        }

        public void SearchSignal()
        {
            if (alsmanager != null && alsmanager.als != null)
            {
                var ss = controlpanel.signalsourcedropdown.captionText.text;
                var s = ss == "All" ? AnalysisExtention.SearchSignal() : ss.Convert<SignalSource>().SearchSignal();
                var sr = false;
                if (s != null)
                {
                    alsmanager.als.Signal = s;
                    sr = true;
                }
                signalpanel.UpdateSignal(sr);
            }
        }

        public void UpdateVisualizationDoneCount(int nvd)
        {
            controlpanel.visualizationdone.text = nvd.ToString();
        }

        public void UpdateAnalysisEventCount(int nae)
        {
            controlpanel.analysiseventdone.text = nae.ToString();
        }

        public void UpdateAnalysisDoneCount(int nad)
        {
            controlpanel.analysisdone.text = nad.ToString();
        }

        public void UpdateEventSyncIntegrity(int esi)
        {
            controlpanel.eventsyncintegrity.isOn = esi == 1 ? true : false;
        }

        public void UpdateEventMeasureIntegrity(int emi)
        {
            controlpanel.eventmeasureintegrity.isOn = emi == 1 ? true : false;
        }

        public void UpdateAnalysisState(bool isanalysing, string msg = null)
        {
            controlpanel.analysisenginestate.isOn = isanalysing;
            controlpanel.analysisstate.text = msg;
        }
    }
}