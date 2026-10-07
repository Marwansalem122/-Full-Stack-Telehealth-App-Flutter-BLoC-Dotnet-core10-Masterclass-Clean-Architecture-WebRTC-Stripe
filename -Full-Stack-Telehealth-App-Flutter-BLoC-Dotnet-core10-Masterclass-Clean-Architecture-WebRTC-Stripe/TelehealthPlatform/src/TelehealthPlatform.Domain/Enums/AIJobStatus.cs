using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Enums
{
    /// <summary>
    /// Pending -> Processing (atomic claim) -> Completed, or Pending after a
    /// bounded retry, or terminal Failed once MaxRetries is reached
    /// (Sequence Diagrams, Document 3, Diagram 3).
    /// </summary>
    public enum AIJobStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }
}
