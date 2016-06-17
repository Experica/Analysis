// --------------------------------------------------------------
// VLEApplicationManager.cs is part of the VLAB project.
// Copyright (c) 2016 All Rights Reserved
// Li Alex Zhang fff008@gmail.com
// 5-16-2016
// --------------------------------------------------------------

using UnityEngine;
using System.Collections.Generic;
using System.IO;
using VLab;

namespace VLabAnalysis
{
    public class VLAApplicationManager : MonoBehaviour
    {
        public VLAUIController uicontroller;
        public Dictionary<string, object> config;
        public readonly string configpath = "VLabAnalysisConfig.yaml";

        void Awake()
        {
            if (File.Exists(configpath))
            {
                config = Yaml.ReadYaml<Dictionary<string, object>>(configpath);
            }
            if(config==null)
            {
                config = new Dictionary<string, object>();
            }
            ValidateConfig();
        }

        void ValidateConfig()
        {
            if (!config.ContainsKey("isautoconn"))
            {
                config["isautoconn"] = true;
            }
            if (!config.ContainsKey("autoconntimeout"))
            {
                config["autoconntimeout"] = 10;
            }
            if (!config.ContainsKey("serveraddress"))
            {
                config["serveraddress"] = "localhost";
            }
            if (!config.ContainsKey("cleardataperanalysis"))
            {
                config["cleardataperanalysis"] = 1;
            }
            if (!config.ContainsKey("defaultanalysissystem"))
            {
                config["defaultanalysissystem"] = "DotNet";
            }
        }

        void OnApplicationQuit()
        {
            if (uicontroller.netmanager.IsClientConnected())
            {
                uicontroller.netmanager.StopClient();
            }
            Yaml.WriteYaml(configpath, config);
        }

    }
}