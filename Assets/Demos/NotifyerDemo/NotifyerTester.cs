using Laubrary.Notifyer;
using Laubrary.Cookbook;
using UnityEngine;
using System;

namespace Laubrary.Notifyer.Samples
{
    public class NotifyerTester : MonoBehaviour
    {
        public string stringEventMessage = "Its happening!";

        void Start()
        {
            Notifyer.Subscribe<MyFunnyEvent>(OnFunnyEvent);
            Notifyer.Subscribe<MyUnhappyEvent>(OnUnhappyEvent);
            Notifyer.Subscribe(stringEventMessage, OnStringEvent);
        }

        public void TriggerHappyEvent()
        {
            Notifyer.Notify(new MyFunnyEvent("Hello from happy event!"), "This was sent: " + DateTime.Now.ToShortTimeString());
        }

        public void TriggerUnHappyEvent()
        {
            Notifyer.Notify(new MyUnhappyEvent("Buhuhuhu!!!" + DateTime.Now.Millisecond));
        }

        public void NotifyStringEvent()
        {
            Notifyer.Notify(stringEventMessage);
        }

        private void OnFunnyEvent(MyFunnyEvent evt)
        {
            Debug.Log($"Received: {evt.Data}");
        }

        private void OnUnhappyEvent(MyUnhappyEvent evt)
        {
            Debug.Log($"Received: {evt.Data}");
        }

        private void OnStringEvent()
        {
            Debug.Log("String event triggered");
        }
    }

    public class MyFunnyEvent : NotifyerEventBase<string>
    {
        public MyFunnyEvent(string data) : base(data) { }
    }

    public class MyUnhappyEvent : NotifyerEventBase<string>
    {
        public MyUnhappyEvent(string data) : base(data) { }
    }
}