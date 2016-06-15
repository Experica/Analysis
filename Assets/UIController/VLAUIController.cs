// --------------------------------------------------------------
// VLEUIController.cs is part of the VLAB project.
// Copyright (c) 2016 All Rights Reserved
// Li Alex Zhang fff008@gmail.com
// 5-16-2016
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using VLab;
using System.Windows.Forms;
using ZedGraph;

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

        private bool isautoconn;
        private int autoconncountdown;
        private float lastautoconntime;

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
            }
        }

        public void OnServerAddressEndEdit(string v)
        {
            appmanager.config["serveraddress"] = v;
        }

        public void OnToggleAutoConnect(bool ison)
        {
            appmanager.config["isautoconn"] = ison;
            ResetAutoConnect();
        }

        public void ResetAutoConnect()
        {
            autoconncountdown = VLConvert.Convert<int>(appmanager.config["autoconntimeout"]);
            isautoconn = VLConvert.Convert<bool>(appmanager.config["isautoconn"]);
            if (!isautoconn)
            {
                autoconntext.text = "Auto Connect OFF";
            }
            autoconn.isOn = isautoconn;
        }

        void Start()
        {
            ResetAutoConnect();
            serveraddress.text = VLConvert.Convert< string>(appmanager.config["serveraddress"]);
        }

        void Update()
        {
            if (isautoconn && !netmanager.IsClientConnected())
            {
                if (Time.unscaledTime - lastautoconntime >= 1)
                {
                    autoconncountdown--;
                    if (autoconncountdown > 0)
                    {
                        autoconntext.text = "Auto Connect " + autoconncountdown + "s";
                        lastautoconntime = Time.unscaledTime;
                    }
                    else
                    {
                        clientconnect.isOn = true;
                        clientconnect.onValueChanged.Invoke(true);
                        isautoconn = false;
                        autoconntext.text = "";
                    }
                }
            }
        }

        public void OnClientDisconnect()
        {
            ResetAutoConnect();
            clientconnect.isOn = false;
        }

    }
}