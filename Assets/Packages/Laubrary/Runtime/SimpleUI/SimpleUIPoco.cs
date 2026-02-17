using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Laubrary.SimpleUI
{
    public abstract class SimpleUIPoco : INotifyPropertyChanged
    {
        private SimpleUIView _boundView;

        public event PropertyChangedEventHandler PropertyChanged;
        
        public event EventHandler<ValueChangedEventArgs> ValueChanged;

        internal void BindToView(SimpleUIView view)
        {
            _boundView = view;
        }

        protected void Refresh()
        {
            _boundView?.UpdateUI(this);
        }

        protected void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            T oldValue = field;
            field = value;
            NotifyPropertyChanged(propertyName, oldValue, value);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            ValueChanged?.Invoke(this, new ValueChangedEventArgs(propertyName, null, null));
            _boundView?.UpdateUI(this);
        }

        private void NotifyPropertyChanged<T>(string propertyName, T oldValue, T newValue)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            ValueChanged?.Invoke(this, new ValueChangedEventArgs(propertyName, oldValue, newValue));
            _boundView?.UpdateUI(this);
        }
    }
}
