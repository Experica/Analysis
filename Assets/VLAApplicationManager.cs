/*
VLAApplicationManager.cs is part of the VLAB project.
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
using System.Collections.Generic;
using System.IO;
using VLab;

namespace VLabAnalysis
{
    public class VLAApplicationManager : MonoBehaviour
    {
        public VLAUIController uicontroller;
        public VLACFG config;
        readonly string configpath = "VLabAnalysisConfig.yaml";

        void Awake()
        {
            Application.runInBackground = true;
            if (File.Exists(configpath))
            {
                config = configpath.ReadYamlFile<VLACFG>();
            }
            if (config == null)
            {
                config = new VLACFG();
            }
        }

        void Start()
        {
            uicontroller.UpdateSystemInformation();
        }

        void OnApplicationQuit()
        {
            if (uicontroller.netmanager.IsClientConnected())
            {
                uicontroller.netmanager.StopClient();
            }
            if (uicontroller.alsmanager != null && uicontroller.alsmanager.als != null)
            {
                uicontroller.alsmanager.als.Dispose();
            }
            configpath.WriteYamlFile(config);
        }

    }
}