using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace MoaiAPIDocParser
{
    class Program
    {
        const string DocBaseUrl = @"http://moaiforge.com/api-docs/latest/";
        const string DocUrl = @"http://moaiforge.com/api-docs/latest/annotated.html";
        const string ID_doccontent = "doc-content";
        const string Node_Name = "a";
        static void Main(string[] args)
        {
            var web = new HtmlWeb();

            Console.WriteLine($"try to connnect {DocUrl} \n");

            var doc = web.Load(DocUrl);
            var docContent = doc.GetElementbyId(ID_doccontent);

            if (doc == null || docContent == null)
            {
                Console.WriteLine($"error when connecting {DocUrl}");
                return;
            }

            List<HtmlNode> trTable = docContent.SelectSingleNode("//table[@class='directory']")
                .Descendants("tr").Where(tr => tr.Elements("td").Count() > 0).ToList();

            Dictionary<string, MoaiAPIData> dic = new Dictionary<string, MoaiAPIData>();

            // 处理类列表文档
            foreach (var tr in trTable)
            {
                if (tr.Elements("td").Count() >= 2)
                {
                    var className = GetClassName(tr.Elements("td").ElementAt(0));
                    var url = GetClassUrl(tr.Elements("td").ElementAt(0));
                    var desc = GetClassDesc(tr.Elements("td").ElementAt(1));

                    if (className != string.Empty && !dic.ContainsKey(className))
                    {
                        var moaiApiData = new MoaiAPIData();
                        moaiApiData.ClassName = className;
                        moaiApiData.Url = url;
                        moaiApiData.ClassDesc = desc;

                        dic.Add(className, moaiApiData);
                    }
                }
            }

            // 处理类成员文档
            foreach (var elm in dic)
            {
                Console.WriteLine("==========================================");
                Console.WriteLine(elm.Key);
                Console.WriteLine("==========================================");

                var classApiContent = web.Load(elm.Value.Url).GetElementbyId(ID_doccontent);
                List<HtmlNode> menItems = classApiContent.SelectNodes("//div[@class='memitem']")?.ToList();
                   
                if (menItems == null)
                {
                    Console.WriteLine($"[Info] class {elm.Key} has no function members.");
                    continue;
                }

                // FIXME: class MOAIGlobalEventSource has bug.
                if (elm.Key == "MOAIGlobalEventSource")
                    continue;

                foreach (var memitem in menItems)
                {
                    // <td class="memname">
                    string funcName = memitem.SelectSingleNode(".//td[@class='memname']").InnerText.Trim();

                    // patch MOAIMoviePlayerIOS + (int) _init(lua_State *)
                    if (elm.Key == "MOAIMoviePlayerIOS")
                        funcName = funcName.Replace("+ (int) _", string.Empty);

                    if (elm.Value.FunctionDic.ContainsKey(funcName))
                    {
                        Console.WriteLine($"[Warning] The same member method signature already exists.({funcName})");
                        continue;
                    }

                    var functionData = new MoaiClassFunctionData();
                    functionData.FunctionName = funcName;
                    // <code>
                    functionData.Code = memitem.SelectSingleNode(".//code").InnerText;

                    Console.WriteLine(funcName);
                    Console.WriteLine(functionData.Code);

                    elm.Value.FunctionDic.Add(funcName, functionData);

                    // <dl class="section return">
                    // <dt>Returns</dt>
                    // <dd>nil </dd> or <dd>number min, number max </dd>
                    string returnInfo =
                        memitem.SelectSingleNode(".//dl[@class='section return']")?.SelectSingleNode("dd")?.InnerText;

                    // <table class="params">
                    List<HtmlNode> paramNodes =
                        memitem.SelectSingleNode(".//table[@class='params']")?
                            .Descendants("tr")?.ToList();

                    if (returnInfo != null)
                    {
                        Console.WriteLine($"Returns {returnInfo}");
                        string[] returnVars = returnInfo.Split(',');
                        foreach (var v in returnVars)
                            if (v.Trim() != "nil")
                                functionData.FunctionReturns.Add(v.Trim());
                    }

                    if (paramNodes == null)
                    {
                        Console.WriteLine();
                        continue;
                    }

                    foreach (var paramsNode in paramNodes)
                    {
                        if (paramsNode.ChildNodes.Count == 0)
                        {
                            Console.WriteLine();
                            continue;
                        }

                        string paramType = paramsNode.ChildNodes[0].InnerText;
                        string paramName = paramsNode.ChildNodes[1].InnerText.Split(' ')[0];
                        functionData.FunctionParams.Add($"{paramType} {paramName}");
                        Console.Write($"{paramType} {paramName}    ");
                    }

                    Console.WriteLine("\n");
                    //Console.WriteLine("\n======================================\n");
                }

                Console.WriteLine("\n");

                //Debug
                //break;
            }

            // 输出MoaiApi.lua文件
            Console.WriteLine("Prepare to write MoaiApi.lua.");

            var fileStream = System.IO.File.CreateText("MoaiApi.lua");

            foreach (var classKV in dic)
            {
                // 输出类声明信息和EmmyLua文档信息
                fileStream.WriteLine($"---@class {classKV.Key}");
                fileStream.WriteLine(classKV.Key + " = { }");
                fileStream.WriteLine();

                foreach (var funcKV in classKV.Value.FunctionDic)
                {
                    // 输出类成员方法和Emmylua文档注释

                    for (var i = 0; i < funcKV.Value.FunctionParams.Count; i++)
                    {
                        var typeName = funcKV.Value.FunctionParams[i].Split(' ')[0];
                        var paramName = funcKV.Value.FunctionParams[i].Split(' ')[1];

                        if (typeName == classKV.Key && paramName == "self")
                            continue;

                        fileStream.WriteLine($"---@param {paramName} {typeName}");
                    }

                    foreach (var returnValue in funcKV.Value.FunctionReturns)
                    {
                        var typeName = returnValue.Split(' ')[0];

                        fileStream.WriteLine($"---@return {typeName}");
                    }

                    bool withSelf = false;
                    string paramsContent = string.Empty;
                    for (var i = 0; i < funcKV.Value.FunctionParams.Count; i++)
                    {
                        var typeName = funcKV.Value.FunctionParams[i].Split(' ')[0];
                        var paramName = funcKV.Value.FunctionParams[i].Split(' ')[1].TrimEnd('.');

                        if (typeName == classKV.Key && paramName == "self")
                        {
                            withSelf = true;
                            continue;
                        }
                        // Fix conflict with lua keyword 'end'
                        if (paramName == "end")
                            paramName = "endd";

                        //paramsContent += funcKV.Value.FunctionParams[i]
                        //    + ((i == funcKV.Value.FunctionParams.Count - 1) ? "" : ", ");
                        paramsContent += paramName
                            + ((i == funcKV.Value.FunctionParams.Count - 1) ? "" : ", ");
                    }

                    var dot = withSelf ? ":" : ".";
                    var contents = $"{classKV.Key}{dot}{funcKV.Key}({paramsContent})";
                    fileStream.WriteLine("function " + contents + " end");
                    fileStream.WriteLine();
                }

                fileStream.WriteLine();
            }
            
            fileStream.Flush();
            fileStream.Close();

            Console.WriteLine("Done!");
        }

        static void DebugHandleClass(HtmlNode td)
        {
#if SHOW_InnerHtml
            Console.WriteLine(td.InnerHtml);
#endif

            bool isDesc = true;

            foreach (var node in td.ChildNodes)
            {
                if (node.Name != Node_Name)
                    continue;
                if (node.Attributes["class"] != null &&
                    node.Attributes["class"].Value == "el" &&
                    node.Attributes["href"] != null)
                {
                    Console.WriteLine(node.InnerText);
                    Console.WriteLine(DocBaseUrl + node.Attributes["href"].Value);
                    isDesc = false;
                }
            }

            if (isDesc)
                Console.WriteLine($"InnerText: {td.InnerText}");

            Console.WriteLine("==========================");
        }

        static string GetClassName(HtmlNode td)
        {
            foreach (var node in td.ChildNodes)
            {
                if (node.Name != Node_Name)
                    continue;
                if (node.Attributes["class"] != null &&
                    node.Attributes["class"].Value == "el" &&
                    node.Attributes["href"] != null)
                {
                    return node.InnerText;
                }
            }

            return string.Empty;
        }

        static string GetClassUrl(HtmlNode td)
        {
            foreach (var node in td.ChildNodes)
            {
                if (node.Name != Node_Name)
                    continue;
                if (node.Attributes["class"] != null &&
                    node.Attributes["class"].Value == "el" &&
                    node.Attributes["href"] != null)
                {
                    //Console.WriteLine(DocBaseUrl + node.Attributes["href"].Value);
                    return DocBaseUrl + node.Attributes["href"].Value;
                }
            }

            return string.Empty;
        }

        static string GetClassDesc(HtmlNode td)
        {
            return td.InnerText;
        }
    }
}
