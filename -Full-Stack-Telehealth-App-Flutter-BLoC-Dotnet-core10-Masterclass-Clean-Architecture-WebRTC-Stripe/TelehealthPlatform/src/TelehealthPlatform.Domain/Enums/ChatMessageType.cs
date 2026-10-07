using System;
using System.Collections.Generic;
using System.Text;

namespace TelehealthPlatform.Domain.Enums
{
    public enum ChatMessageType
    {
        Text,
        /// <summary>
        /// Server-generated only (e.g. "Dr. Ahmed joined the call") — never
        /// set from a client-supplied value (API Contract, SendMessage validation).
        /// </summary>
        System
    }
}
