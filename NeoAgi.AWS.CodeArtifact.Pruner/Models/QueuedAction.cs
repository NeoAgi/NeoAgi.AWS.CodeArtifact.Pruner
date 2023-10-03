using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    internal class QueuedAction
    {
        public int AttemptedCount { get; protected set; } = 0;
        public Guid ActionID { get; protected set; } = Guid.NewGuid();

        public QueuedAction() { }

        public void IncreaseAttempted()
        {
            AttemptedCount++;
        }
    }
}
