using System;

namespace Laubrary.SimpleUI
{
    public class BindingReport
    {
        public string FieldName;
        public Type FieldType;
        public BindingStatus Status;
        public string ResolverStep;
        public string GameObjectPath;
        public Type ComponentType;
        public BindingMode Mode;
        public ConfidenceLevel Confidence;
        public string[] DecisionTrace;
        public string ErrorMessage;
        public string[] SuggestedFixes;
    }
}
