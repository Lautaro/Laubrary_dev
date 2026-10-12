using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>A discrete choice rendered as a number: relative drag, click to type, double-click reset.</summary>
    public sealed class ZuiScrubNumber : VisualElement
    {
        readonly int min, max, reset;
        readonly Action<int> changed;
        readonly Label number;
        readonly IntegerField input;
        int current, startValue, pointer=-1;
        float startX;
        bool moved;
        public int value { get=>current; set=>SetValueWithoutNotify(value); }
        public ZuiScrubNumber(string label,int value,int min,int max,string tooltip,Action<int> changed,int defaultValue=0)
        {
            this.min=min;this.max=Math.Max(min,max);reset=Mathf.Clamp(defaultValue,min,this.max);this.changed=changed;
            this.tooltip=tooltip+" Drag horizontally to choose. Click to type. Double-click resets to "+reset+".";
            AddToClassList("zui-scrubnumber");focusable=true;
            var caption=new Label(label){pickingMode=PickingMode.Ignore};caption.AddToClassList("zui-scrubnumber__label");Add(caption);
            number=new Label(){pickingMode=PickingMode.Ignore};number.AddToClassList("zui-scrubnumber__number");Add(number);
            input=new IntegerField(){isDelayed=true};input.AddToClassList("zui-scrubnumber__input");input.style.display=DisplayStyle.None;Add(input);
            input.RegisterCallback<PointerDownEvent>(e=>{if(e.clickCount==2){Commit(reset);EndTyping();Focus();}e.StopPropagation();});
            input.RegisterValueChangedCallback(e=>{Commit(e.newValue);EndTyping();});
            input.RegisterCallback<FocusOutEvent>(e=>EndTyping());
            input.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){EndTyping();Focus();e.StopPropagation();}});
            RegisterCallback<PointerDownEvent>(Down);RegisterCallback<PointerMoveEvent>(Move);RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCaptureOutEvent>(e=>pointer=-1);
            RegisterCallback<KeyDownEvent>(e=>{if(e.target==input||input.Contains(e.target as VisualElement))return;if(e.keyCode==KeyCode.Return){BeginTyping();e.StopPropagation();}else if(e.keyCode==KeyCode.LeftArrow||e.keyCode==KeyCode.RightArrow){Commit(current+(e.keyCode==KeyCode.RightArrow?1:-1));e.StopPropagation();}});
            SetValueWithoutNotify(value);
        }
        public void SetValueWithoutNotify(int value){current=Mathf.Clamp(value,min,max);number.text=current.ToString();input.SetValueWithoutNotify(current);EnableInClassList("zui-scrubnumber--changed",current!=reset);}
        void Commit(int v){int old=current;SetValueWithoutNotify(v);if(old!=current)changed?.Invoke(current);}
        void BeginTyping(){input.SetValueWithoutNotify(current);number.style.display=DisplayStyle.None;input.style.display=DisplayStyle.Flex;input.Focus();input.SelectAll();}
        void EndTyping(){input.style.display=DisplayStyle.None;number.style.display=DisplayStyle.Flex;}
        void Down(PointerDownEvent e)
        {
            if(e.button!=0)return;
            if(e.clickCount==2){Commit(reset);EndTyping();e.StopPropagation();return;}
            Focus();pointer=e.pointerId;startX=e.position.x;startValue=current;moved=false;this.CapturePointer(pointer);e.StopPropagation();
        }
        void Move(PointerMoveEvent e)
        {
            if(pointer!=e.pointerId)return;float delta=e.position.x-startX;if(Mathf.Abs(delta)>3)moved=true;
            if(moved)SetValueWithoutNotify(startValue+Mathf.RoundToInt(delta/(e.shiftKey?24f:8f)));e.StopPropagation();
        }
        void Up(PointerUpEvent e)
        {
            if(pointer!=e.pointerId)return;pointer=-1;this.ReleasePointer(e.pointerId);
            if(moved){int final=current;if(final!=startValue)changed?.Invoke(final);}else BeginTyping();e.StopPropagation();
        }
    }
}
