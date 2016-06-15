// --------------------------------------------------------------
// VLENetManager.cs is part of the VLAB project.
// Copyright (c) 2016 All Rights Reserved
// Li Alex Zhang fff008@gmail.com
// 5-16-2016
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using VLab;

namespace VLabAnalysis
{
    public class VLANetManager : NetworkManager
    {
        public VLAUIController uicontroller;
        public GameObject vlabanalysismanagerprefab;

        void RegisterSpawnHandler()
        {
            vlabanalysismanagerprefab = Resources.Load<GameObject>("VLAnalysisManager");
            var assetid = vlabanalysismanagerprefab.GetComponent<NetworkIdentity>().assetId;
            ClientScene.RegisterSpawnHandler(assetid, new SpawnDelegate(SpawnHandler), new UnSpawnDelegate(UnSpawnHandler));
        }

        public override void OnStartClient(NetworkClient client)
        {
            client.RegisterHandler(MsgType.Scene, new NetworkMessageDelegate(OnClientScene));
            RegisterSpawnHandler();
        }

        void OnClientScene(NetworkMessage netMsg)
        {
            if (IsClientConnected() && !NetworkServer.active)
            {
                OnClientSceneChanged(client.connection);
            }
        }

        GameObject SpawnHandler(Vector3 position, NetworkHash128 assetId)
        {
            GameObject go;
            if(uicontroller.alsmanager==null)
            {
                go = Instantiate(vlabanalysismanagerprefab);
                go.name = "VLAnalysisManager";
                go.transform.SetParent(transform);
                var als = go.GetComponent<VLAnalysisManager>();
                uicontroller.alsmanager = als;
                als.uicontroller = uicontroller;
            }
            else
            {
                go = uicontroller.alsmanager.gameObject;
            }
            return go;
        }

        void UnSpawnHandler(GameObject spawned)
        {
        }

        public override void OnClientConnect(NetworkConnection conn)
        {
            if (LogFilter.logDebug)
            {
                UnityEngine.Debug.Log("Send PeerType Message.");
            }
            client.Send(VLMsgType.PeerType, new IntegerMessage((int)VLPeerType.VLabAnalysis));

            Time.fixedDeltaTime = 0.0001f;
            Process.GetCurrentProcess().PriorityBoostEnabled = true;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }

        public override void OnClientDisconnect(NetworkConnection conn)
        {
            base.OnClientDisconnect(conn);
            uicontroller.OnClientDisconnect();

            Time.fixedDeltaTime = 0.02f;
            Process.GetCurrentProcess().PriorityBoostEnabled = false;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }

    }
}