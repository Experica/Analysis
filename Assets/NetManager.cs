/*
NetManager.cs is part of the Experica.
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
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using System.Collections;
using System.Diagnostics;
using System.Threading;

namespace Experica.Analysis
{
    public class NetManager : NetworkManager
    {
        public UIController uicontroller;
        public GameObject analysismanagerprefab, controlmanagerprefab;

        void Start()
        {
            var assetida = analysismanagerprefab.GetComponent<NetworkIdentity>().assetId;
            ClientScene.RegisterSpawnHandler(assetida, new SpawnDelegate(AnalysisManagerSpawnHandler),
                new UnSpawnDelegate(AnalysisManagerUnSpawnHandler));

            var assetidc = controlmanagerprefab.GetComponent<NetworkIdentity>().assetId;
            ClientScene.RegisterSpawnHandler(assetidc, new SpawnDelegate(ControlManagerSpawnHandler),
                new UnSpawnDelegate(ControlManagerUnSpawnHandler));
        }

        /// <summary>
        /// Prepare client so that when connected to server, will react properly to server commands.
        /// </summary>
        /// <param name="client"></param>
        public override void OnStartClient(NetworkClient client)
        {
            // override default handler with our own to deal with server's ChangeScene message.
            client.RegisterHandler(MsgType.Scene, new NetworkMessageDelegate(OnClientScene));
        }

        /// <summary>
        /// our custom handler for server's ChangeScene message, since VLabAnalysis doesn't deal
        /// with any scene, we just ingore the message, pretending that scene has already been loaded
        /// and immediatly tell server we have changed the scene and ready to proceed.
        /// </summary>
        /// <param name="netMsg"></param>
        void OnClientScene(NetworkMessage netMsg)
        {
            OnClientSceneChanged(client.connection);
        }

        GameObject AnalysisManagerSpawnHandler(Vector3 position, NetworkHash128 assetId)
        {
            GameObject go = Instantiate(analysismanagerprefab);
            var am = go.GetComponent<AnalysisManager>();
            am.uicontroller = uicontroller;
            uicontroller.alsmanager = am;
            go.name = "AnalysisManager";
            go.transform.SetParent(transform, false);

            uicontroller.OnAnalysisManagerSpwaned();
            return go;
        }

        void AnalysisManagerUnSpawnHandler(GameObject spawned)
        {
        }

        GameObject ControlManagerSpawnHandler(Vector3 position, NetworkHash128 assetId)
        {
            GameObject go;
            if (uicontroller.ctrlmanager == null)
            {
                go = Instantiate(controlmanagerprefab);
                var cm = go.GetComponent<ControlManager>();
                cm.uicontroller = uicontroller;
                uicontroller.ctrlmanager = cm;
                go.name = "ControlManager";
                go.transform.SetParent(transform, false);
            }
            else
            {
                go = uicontroller.ctrlmanager.gameObject;
            }
            uicontroller.OnControlManagerSpwaned();
            return go;
        }

        void ControlManagerUnSpawnHandler(GameObject spawned)
        {
        }
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          
        /// <summary>
        /// because the difference of Analysis and Environment, Command should treat them
        /// differently, so whenever a client connected to server, it seeds client information.
        /// </summary>
        /// <param name="conn"></param>
        public override void OnClientConnect(NetworkConnection conn)
        {
            if (LogFilter.logDebug)
            {
                UnityEngine.Debug.Log("Send PeerType Message.");
            }
            client.Send(MsgType.PeerType, new IntegerMessage((int)PeerType.Analysis));
            ClientScene.AddPlayer(conn, 0);
            uicontroller.OnClientConnect();
        }

        public override void OnClientDisconnect(NetworkConnection conn)
        {
            base.OnClientDisconnect(conn);
            uicontroller.OnClientDisconnect();
        }

        public override void OnStopClient()
        {
            NetworkClient.ShutdownAll();
            client = null;
        }
    }
}