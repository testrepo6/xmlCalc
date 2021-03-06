﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;

namespace xmlCalc
{
    class Program
    {
        static void Main (string[] args)
        {
            Stopwatch watch = Stopwatch.StartNew ();
            watch.Start ();

            string procDir;
            if (args.Length == 0 || !Directory.Exists (args[0]))
            {
                Console.WriteLine ("Каталог не задан или не существует.");
                return;
            }
            else
                procDir = args[0];

            string[] xmlFiles = Directory.GetFiles (procDir, "*.xml");
            int filesCount = xmlFiles.Length;
            if (filesCount == 0)
            {
                Console.WriteLine ("В указанном каталоге нет xml-файлов.");
                return;
            }

            XmlThreadClass[] threadData = new XmlThreadClass[filesCount];
            ManualResetEvent[] resetEvents = new ManualResetEvent[filesCount];
            for (int i = 0; i < filesCount; i++)
            {
                resetEvents[i] = new ManualResetEvent (false);
                threadData[i] = new XmlThreadClass { file = xmlFiles[i], resetEvent = resetEvents[i] };
                ThreadPool.QueueUserWorkItem (threadData[i].ProcXml);
            }
            WaitHandle.WaitAll (resetEvents);

            if (XmlThreadClass.dictErrors.Count > 0)
            {
                Console.WriteLine ("ошибки:");
                foreach (KeyValuePair<string, string> dictPair in XmlThreadClass.dictErrors)
                    Console.WriteLine ($"{dictPair.Key}: {dictPair.Value}");
                Console.WriteLine ();
            }

            if (XmlThreadClass.dictSuccesses.Count > 0)
            {
                var maxCalcFiles = new List<string> ();
                int maxCalcCount = 0;
                Console.WriteLine ("результаты вычислений:");
                foreach (var dictPair in XmlThreadClass.dictSuccesses)
                {
                    string file = dictPair.Key;
                    CalcSuccessData calcSuccData = dictPair.Value;
                    int result = calcSuccData.result;
                    if (calcSuccData.count > maxCalcCount)
                    {
                        maxCalcFiles.Clear ();
                        maxCalcFiles.Add (file);
                        maxCalcCount = calcSuccData.count;
                    }
                    else if (calcSuccData.count == maxCalcCount && maxCalcFiles.Count > 0)
                        maxCalcFiles.Add (file);

                    Console.WriteLine ($"{file}: {result}");
                }
                if (XmlThreadClass.dictSuccesses.Count > 1)
                {
                    string strMaxCalcFiles = String.Join (", ", maxCalcFiles);
                    Console.WriteLine ("\nмаксимальное количество правильных calculation: {0}\n{1}",
                        maxCalcCount, strMaxCalcFiles);
                }
                Console.WriteLine ();
            }

            watch.Stop ();
            Console.WriteLine ($"время выполнения: {watch.Elapsed}");
        }
    }

    enum EnumOp { add, subtract, multiply, divide, mod };

    struct Calc
    {
        public EnumOp op;
        public int num;
    }

    struct CalcSuccessData
    {
        public int result, count;
    }

    class XmlThreadClass
    {
        public string file;
        public ManualResetEvent resetEvent;

        static public ConcurrentDictionary<string, CalcSuccessData> dictSuccesses =
            new ConcurrentDictionary<string, CalcSuccessData> ();
        static public ConcurrentDictionary<string, string> dictErrors =
            new ConcurrentDictionary<string, string> ();

        static Dictionary<string, EnumOp> dictOp = new Dictionary<string, EnumOp> {
                { "add", EnumOp.add },
                { "subtract", EnumOp.subtract },
                { "multiply", EnumOp.multiply },
                { "divide", EnumOp.divide },
                { "mod", EnumOp.mod }
        };

        public void ProcXml (object state)
        {
            XmlDocument xmlDoc = new XmlDocument ();
            try
            {
                xmlDoc.Load (file);
            }
            catch (XmlException)
            {
                dictErrors.TryAdd (file, "ошибка загрузки или обработки XML");
                resetEvent.Set ();
                return;
            }
            string xpath = "/folder/folder [@name='calculations']/folder [@name='calculation']";
            XmlNodeList calcFolders = xmlDoc.SelectNodes (xpath);
            if (calcFolders.Count == 0)
            {
                dictErrors.TryAdd (file, "нет нужных элементов");
                resetEvent.Set ();
                return;
            }

            int calcResult = 0, calcCount = 0;

            foreach (XmlNode calcFolder in calcFolders)
            {
                string strCalcOp, strNum;
                strCalcOp = strNum = "";
                int calcAttrCount = 0;

                foreach (XmlNode paramNode in calcFolder.ChildNodes)
                {
                    string attrName, attrValue;
                    attrName = attrValue = "";

                    foreach (XmlAttribute paramAttr in paramNode.Attributes)
                    {
                        if (paramAttr.Name == "name")
                            attrName = paramAttr.Value;
                        else if (paramAttr.Name == "value")
                            attrValue = paramAttr.Value;
                    }

                    if (attrName == "" || attrValue == "")
                        continue;
                    else if (attrName == "operand")
                    {
                        calcAttrCount++;
                        strCalcOp = attrValue;
                    }
                    else if (attrName == "mod")
                    {
                        calcAttrCount++;
                        strNum = attrValue;
                    }
                }

                int calcNum = 0;
                bool calcParamsCorrect =
                    calcAttrCount == 2 && strCalcOp != "" && strNum != "" &&
                    dictOp.ContainsKey (strCalcOp) && int.TryParse (strNum, out calcNum);
                if (!calcParamsCorrect)
                    continue;

                Calc calc;
                calc.op = dictOp[strCalcOp];
                calc.num = calcNum;

                switch (calc.op)
                {
                case EnumOp.add: calcResult += calc.num; break;
                case EnumOp.subtract: calcResult -= calc.num; break;
                case EnumOp.multiply: calcResult *= calc.num; break;
                case EnumOp.divide: calcResult /= calc.num; break;
                case EnumOp.mod: calcResult %= calc.num; break;
                }
                calcCount++;
            }
            
            CalcSuccessData calcSuccData;
            calcSuccData.result = calcResult;
            calcSuccData.count = calcCount;
            dictSuccesses.TryAdd (file, calcSuccData);
            resetEvent.Set ();
        }
    }
}
