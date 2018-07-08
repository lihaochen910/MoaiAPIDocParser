using System.Collections.Generic;

namespace MoaiAPIDocParser
{
    public class MoaiAPIData
    {
        public string ClassName;
        public string Url;
        public string ClassDesc;

        public Dictionary<string, MoaiClassFunctionData> FunctionDic;
        public MoaiAPIData()
        {
            FunctionDic = new Dictionary<string, MoaiClassFunctionData>();
        }
    }

    public class MoaiClassFunctionData
    {
        public string FunctionName;
        public string Code;
        public List<string> FunctionParams;
        public List<string> FunctionReturns;

        public MoaiClassFunctionData()
        {
            FunctionParams = new List<string>();
            FunctionReturns = new List<string>();
        }
    }

    public class SublimeCompletionsFile
    {
        public string scope = "source.luae, text.lua - source - meta.tag, punctuation.definition.tag.begin";
        public string version = "0.1";
        public List<CompletionData> completions;

        public SublimeCompletionsFile()
        {
            completions = new List<CompletionData>();
        }

        public class CompletionData
        {
            public string trigger;
            public string contents;
        }
    }
}
