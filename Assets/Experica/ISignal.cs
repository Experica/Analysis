/*
ISignal.cs is part of the Experica.
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
using System;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace Experica
{
    public enum SignalSource
    {
        IExSys,
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

    public readonly struct SignalDescription
    {
        public readonly int Channel;
        public readonly SignalType Type;

        public SignalDescription(int channel, SignalType type)
        {
            Channel = channel;
            Type = type;
        }
    }

    public interface ISignal : IDisposable
    {
        SignalSource Source { get; }
        bool Connect();
        void Close();
        void RefreshChannels();
        ImmutableArray<int> Channels { get; }
        bool IsChannel { get; }
        double Time { get; }
        ImmutableArray<SignalType> GetSignalTypes(int channel, bool onlyreturnsignalontype);
        bool IsSignalOn(int channel, SignalType signaltype);
        bool IsRunning { get; }
        bool Start(bool isclean);
        bool Stop(bool collectallbeforestop);
        bool Restart(bool iscleanall);
        void Read(out Dictionary<int, List<double>> spike, out Dictionary<int, List<int>> uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out Dictionary<int, List<double>> dintime, out Dictionary<int, List<int>> dinvalue);
        int CacheMaxDuration { get; set; }
    }
}
