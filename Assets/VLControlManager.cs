using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace VLabAnalysis
{
    [NetworkSettings(channel = 0, sendInterval = 0)]
    public class VLControlManager : NetworkBehaviour
    {
        public VLAUIController uicontroller;

        [Command]
        public void CmdRF()
        {

        }


    }
}
