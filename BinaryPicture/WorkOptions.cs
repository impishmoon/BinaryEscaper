using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BinaryEscaper
{
    class WorkOptions
    {
        public enum Operation
        {
            Unset,
            Encode,
            Decode
        }

        public Operation operation = Operation.Unset;
        public string inputPath = "";
        public string outputPath = "";
        public bool compress = true;

        public bool IsValid()
        {
            if (operation == Operation.Unset) return false;
            if (inputPath == "") return false;

            return true;
        }
    }
}
