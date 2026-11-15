using TMProOld;
using UnityEngine;

namespace Silksong.CoopAssist
{
    // One renderer adapter for initial creation and later repairs after native UI/skin changes.
    internal static class NameLabelStyle
    {
        internal const float PointSize=27;
        internal static void ApplyFont(TextMeshPro label,TMP_FontAsset face)
        {
            if(label==null)return;
            bool changed=false;
            if(face!=null && label.font!=face){label.font=face;changed=true;}
            if(label.enableAutoSizing){label.enableAutoSizing=false;changed=true;}
            if(label.fontSize!=PointSize){label.fontSize=PointSize;changed=true;}
            if(changed)label.havePropertiesChanged=true;
        }
        internal static void ApplyWeight(TextMeshPro label,bool bold)
        {
            if(label==null)return;
            var style=bold?FontStyles.Bold:FontStyles.Normal;
            float outline=bold?.4f:.2f;
            bool changed=false;
            if(label.fontStyle!=style){label.fontStyle=style;changed=true;}
            if(label.outlineWidth!=outline){label.outlineWidth=outline;changed=true;}
            if(changed)label.havePropertiesChanged=true;
        }
        internal static void ApplyColour(TextMeshPro label,Color tint)
        {
            if(label==null || label.color==tint)return;
            label.color=tint;label.havePropertiesChanged=true;
        }
    }
}
