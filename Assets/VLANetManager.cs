/*
VLANetManager.cs is part of the VLAB project.
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
using VLab;

namespace VLabAnalysis
{
    public class VLANetManager : NetworkManager
    {
        public VLAUIController uicontroller;
        GameObject vlabanalysismanagerprefab;

        void RegisterSpawnHandler()
        {
            vlabanalysismanagerprefab = Resources.Load<GameObject>("VLAnalysisManager");
            var assetid = vlabanalysismanagerprefab.GetComponent<NetworkIdentity>().assetId;
            ClientScene.RegisterSpawnHandler(assetid, new SpawnDelegate(AnalysisManagerSpawnHandler),
                new UnSpawnDelegate(AnalysisManagerUnSpawnHandler));
        }

        /// <summary>
        /// Prepare network so that when connected to server, will react properly to 
        /// server commands
        /// </summary>
        /// <param name="client"></param>
        public override void OnStartClient(NetworkClient client)
        {
            // override default handler with our own to deal with server's ChangeScene message.
            client.RegisterHandler(MsgType.Scene, new NetworkMessageDelegate(OnClientScene));
            RegisterSpawnHandler();
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
            GameObject go;
            if (uicontroller.alsmanager == null)
            {
                go = Instantiate(vlabanalysismanagerprefab);
                var am = go.GetComponent<VLAnalysisManager>();
                am.uicontroller = uicontroller;
                uicontroller.alsmanager = am;
                go.name = "VLAnalysisManager";
                go.transform.SetParent(transform, false);
            }
            else
            {
                go = uicontroller.alsmanager.gameObject;
            }
            uicontroller.OnAnalysisManagerSpwaned();
            return go;
        }

        void AnalysisManagerUnSpawnHandler(GameObject spawned)
        {
        }

        /// <summary>
        /// because the fundamental difference of VLabAnalysis and VLabEnvironment, VLab should treat them
        /// differently, so whenever a client connected to server, it seeds information about the client, so
        /// that server could treat them accordingly.
        /// </summary>
        /// <param name="conn"></param>
        public override void OnClientConnect(NetworkConnection conn)
        {
            if (LogFilter.logDebug)
            {
                UnityEngine.Debug.Log("Send PeerType Message.");
            }
            client.Send(VLMsgType.PeerType, new IntegerMessage((int)VLPeerType.VLabAnalysis));
            uicontroller.OnClientConnect();
        }

        public override void OnClientDisconnect(NetworkConnection conn)
        {
            base.OnClientDisconnect(conn);
            uicontroller.OnClientDisconnect();
        }

    }
}