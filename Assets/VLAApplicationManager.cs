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
    public enum VLACFG
    {
        AutoConnect,
        AutoConnectTimeOut,
        ServerAddress,
        ClearDataPerAnalysis,
        DefaultAnalysisSystem
    }

    public class VLAApplicationManager : MonoBehaviour
    {
        public VLAUIController uicontroller;
        public Dictionary<VLACFG, object> config;
        public readonly string configpath = "VLabAnalysisConfig.yaml";

        void Awake()
        {
            if (File.Exists(configpath))
            {
                config = Yaml.ReadYaml<Dictionary<VLACFG, object>>(configpath);
            }
            if(config==null)
            {
                config = new Dictionary<VLACFG, object>();
            }
            ValidateConfig();
        }

        void ValidateConfig()
        {
            if (!config.ContainsKey(VLACFG.AutoConnect))
            {
                config[VLACFG.AutoConnect] = true;
            }
            else
            {
                config[VLACFG.AutoConnect] = config[VLACFG.AutoConnect].Convert<bool>();
            }
            if (!config.ContainsKey(VLACFG.AutoConnectTimeOut))
            {
                config[VLACFG.AutoConnectTimeOut] = 10;
            }
            else
            {
                config[VLACFG.AutoConnectTimeOut] = config[VLACFG.AutoConnectTimeOut].Convert<int>();
            }
            if (!config.ContainsKey(VLACFG.ServerAddress))
            {
                config[VLACFG.ServerAddress] = "localhost";
            }
            if (!config.ContainsKey(VLACFG.ClearDataPerAnalysis))
            {
                config[VLACFG.ClearDataPerAnalysis] = 1;
            }
            else
            {
                config[VLACFG.ClearDataPerAnalysis] = config[VLACFG.ClearDataPerAnalysis].Convert<int>();
            }
            if (!config.ContainsKey(VLACFG.DefaultAnalysisSystem))
            {
                config[VLACFG.DefaultAnalysisSystem] = AnalysisSystem.DotNet;
            }
            else
            {
                config[VLACFG.DefaultAnalysisSystem] = config[VLACFG.DefaultAnalysisSystem].Convert<AnalysisSystem>();
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