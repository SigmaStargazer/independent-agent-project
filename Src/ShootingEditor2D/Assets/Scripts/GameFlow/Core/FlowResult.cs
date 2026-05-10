using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameFlow
{
    public class FlowResult
    {
        public bool Success;
        public string Error;

        public static FlowResult Ok()
        {
            return new FlowResult
            {
                Success = true
            };
        }

        public static FlowResult Fail(string err)
        {
            return new FlowResult
            {
                Success = false,
                Error = err
            };
        }
    }
}
