using System;
using System.Runtime.Serialization;

namespace Microsoft.CodeAnalysis.Diff
{
    [Serializable]
    internal class MergeFailedException : Exception
    {
        public MergeFailedException()
        {
        }

        public MergeFailedException(string message) : base(message)
        {
        }

        public MergeFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MergeFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}