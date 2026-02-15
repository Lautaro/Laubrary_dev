namespace Laubrary.SimpleUI
{
    public abstract class SimpleUIPoco
    {
        private SimpleUIView _boundView;

        internal void BindToView(SimpleUIView view)
        {
            _boundView = view;
        }

        protected void Refresh()
        {
            _boundView?.UpdateUI(this);
        }
    }
}
