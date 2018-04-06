using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VLab
{
    public enum EnvironmentObject
    {
        None,
        Quad,
        GratingQuad,
        ImageQuad
    }

    public enum MaskType
    {
        None,
        Disk,
        Gaussian,
        DiskFade
    }

    public enum OnOff
    {
        On,
        Off
    }

    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft
    }

    public enum GratingType
    {
        Square,
        Sinusoidal,
        Linear
    }
}