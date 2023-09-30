using System;
using System.Collections.Generic;
using System.Text;

namespace NeoAgi.AWS.CodeArtifact.Pruner.Models
{
    /// <summary>
    /// Enum Extensions and Utilities
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Enum<T> where T : struct, IConvertible
    {
        /// <summary>
        /// Parses an Enum from the provided Enum, defaulting if a match cannot be made
        /// </summary>
        /// <param name="val">String to parse</param>
        /// <param name="defaultVal">Enum to search within</param>
        /// <param name="caseInsensitve">Perform parsing case insensitive</param>
        /// <returns></returns>
        public static T ParseOrDefault(string val, T defaultVal, bool caseInsensitve = true)
        {
            if (!Enum.TryParse(val, caseInsensitve, out T parsed))
            {
                parsed = defaultVal;
            }

            return parsed;
        }
    }
}