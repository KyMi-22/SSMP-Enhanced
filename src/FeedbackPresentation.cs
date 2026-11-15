using System;
using Silksong.ModMenu.Elements;
using Silksong.ModMenu.Screens;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        readonly AssistFeedback feedback = new AssistFeedback();
        readonly GUIContent feedbackContent = new GUIContent();
        GUIStyle feedbackStyle;
        const float FeedbackFooterHeight = 112;

        GUIStyle FeedbackStyle
        {
            get
            {
                if(feedbackStyle == null)
                {
                    feedbackStyle = new GUIStyle(GUI.skin.label)
                    {
                        wordWrap = true,
                        richText = false,
                        fontSize = 15,
                        alignment = TextAnchor.UpperLeft
                    };
                    feedbackStyle.normal.textColor = Color.white;
                }
                return feedbackStyle;
            }
        }

        void DrawOptionsFeedback()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(FeedbackFooterHeight));
            GUILayout.Label("操作提示");
            GUILayout.Label(feedback.Message, FeedbackStyle);
            GUILayout.EndVertical();
        }

        void DrawFeedbackToast()
        {
            float width=Mathf.Min(560,Screen.width-40);
            float bottom=Screen.height-20;
            var notices=feedback.Notices;
            // Newest is closest to the bottom-left edge. A modal shows only its latest result.
            for(int i=notices.Count-1;i>=0;i--)
            {
                if(!notices[i].IsRecent(Time.unscaledTime))continue;
                feedbackContent.text=notices[i].Message;
                float height=FeedbackStyle.CalcHeight(feedbackContent,width-24)+24;
                var rect=new Rect(20,Mathf.Max(12,bottom-height),width,height);
                var previous=GUI.color;
                GUI.color=new Color(.08f,.08f,.08f,.96f);
                GUI.DrawTexture(rect,Texture2D.whiteTexture);
                GUI.color=previous;
                GUI.Label(new Rect(rect.x+12,rect.y+12,width-24,height-24),feedbackContent,FeedbackStyle);
                bottom=rect.y-6;
                if(travelPickerOpen || bottom<80)break;
            }
        }

        void AddMenuFeedback(ScrollingMenuScreen menu)
        {
            var label = new TextLabel("操作提示：" + feedback.Message);
            label.RectTransform.sizeDelta = new Vector2(1000, 120);
            label.Text.fontSize = 24;
            label.Text.supportRichText = false;
            label.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.Text.alignment = TextAnchor.MiddleLeft;
            Action<string> update = message => {if(label.Text!=null)label.Text.text = "操作提示：" + message;};
            feedback.Changed += update;
            menu.OnShow += _ => update(feedback.Message);
            menu.OnDispose += () => feedback.Changed -= update;
            menu.Add(label);
        }
    }
}
