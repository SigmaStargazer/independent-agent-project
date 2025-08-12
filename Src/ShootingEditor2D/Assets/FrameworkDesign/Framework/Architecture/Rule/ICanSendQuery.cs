using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FrameworkDesign
{
    public interface ICanSendQuery : IBelongToArchitecture
    {

    }

    public static class CanSendQueryExtension
    {
        public static T SendQuery<T>(this ICanSendQuery self, IQuery<T> query)
        {
            return self.GetArchitecture().SendQuery(query);
        }
    }
}

