using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VLabAnalysis
{
    public enum SignalSource
    {
        Ripple,
        Plexon,
        TDT
    }

    public enum SignalType
    {
        Raw,
        ThirtyKSpS,
        TwoKSpS,
        OneKSpS,
        LFP,
        Event,
        Spike,
        Stim
    }

    public struct Signal
    {
        public int Channel { get; set; }
        public SignalType Type { get; set; }

        public Signal(int channel, SignalType type)
        {
            Channel = channel;
            Type = type;
        }
    }

    /// <summary>
    /// Implementation should be thread safe
    /// </summary>
    public interface ISignal : IDisposable
    {
        bool IsOnline { get; }
        SignalSource Source { get; }
        int[] Channels { get; }
        SignalType[] GetSignalTypes(int channel, bool isonlyreturnonsignaltype);
        bool IsSignalOn(int channel, SignalType signaltype);
        double Time { get; }
        bool IsReady { get; }
        void Start(bool isclean);
        void Stop(bool iscollectallbeforestop);
        void Restart(bool iscleanall);
        void Read(out List<double>[] spike, out List<int>[] uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out List<double>[] dintime, out List<int>[] dinvalue);
    }
}
