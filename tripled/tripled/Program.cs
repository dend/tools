using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using tripled.Kernel;
using tripled.Models;

namespace tripled
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Logger logger = null;

            Console.WriteLine("Document De-Duper | Build Version: {0}", version);

            Parser.Default.ParseArguments<CommandLineOptions>(args).WithParsed(options =>
            {
                logger = new Logger(options.EnableLogging, options.LogLevel);

                try
                {
                    Run(options, logger);
                }
                finally
                {
                    logger.Flush();
                }
                
            });
        }

        private static void Run(CommandLineOptions options, Logger logger)
        {
            var analyzer = new Analyzer();
            if (!string.IsNullOrWhiteSpace(options.XmlPath))
            {
                // We have the XML path, so we can proceed.
                // NOTE: There is no need to error out if there is no XML path specified because
                // CommandLineParser will automatically throw an error.

                var filesToAnalyze = new List<string>(analyzer.GetFilesToAnalyze(options.XmlPath));
                if (filesToAnalyze.Count <= 0) return;
                logger.Log($"Detected {filesToAnalyze.Count} files.");

                // Build out the cache of DocIDs.
                var frameworkFiles = Directory.GetFiles(Path.Combine(options.XmlPath, "FrameworksIndex"),
                    "*.xml", SearchOption.AllDirectories);
                var docIdCache = new List<string>();

                foreach (var file in frameworkFiles)
                {
                    var frameworkFile = XDocument.Load(file);
                    var docIds = from c in frameworkFile.Descendants()
                                 where c.Attribute("Id") != null
                                 select c.Attribute("Id")?.Value;
                    docIdCache.AddRange(docIds);
                }

                foreach (var file in filesToAnalyze)
                {
                    logger.Log($"Analyzing file: {file}");

                    var xml = XDocument.Load(file);

                    if (xml.Root == null) continue;
                    var elementSet = xml.Root.XPathSelectElements("/Type/Members/Member");
                    var elementsToRemove = new List<XElement>();
                    for (var i = 0; i < elementSet.Count(); i++)
                    {
                        var targetSignature = elementSet.ElementAt(i).Descendants("MemberSignature")
                            .FirstOrDefault(el => el.Attribute("Language")?.Value == "DocId")
                            ?.Attribute("Value")
                            .Value;
                        logger.Log($"De-duping signature: {targetSignature}", TraceLevel.Verbose);

                        var dupedElements = from xe
                                in elementSet
                                            where xe.Descendants("MemberSignature").FirstOrDefault(el =>
                                                      el.Attribute("Language")?.Value == "DocId" &&
                                                      el.Attribute("Value")?.Value == targetSignature) != null
                                            select xe;

                        
                        var logEntry = $"Elements with matching signature: {dupedElements.Count()}";
                        if (dupedElements.Count() == 1)
                        {
                            logEntry += $", {targetSignature} is CLEAN";
                            logger.Log(logEntry, TraceLevel.Verbose);
                        }
                        else
                        {
                            logEntry += $", {targetSignature} is DIRTY";
                            logger.Log(logEntry, TraceLevel.Warning);

                            elementsToRemove.AddRange(analyzer.PickLosingElements(dupedElements));
                        }

                        if (!elementsToRemove.Any()) continue;
                        foreach (var removableElement in elementsToRemove)
                        {
                            var element = from e in xml.Root.Descendants("Member")
                                          where XNode.DeepEquals(e, removableElement)
                                          select e;

                            element.Remove();
                        }
                    }

                    ProcessDupeContent(xml);

                    var shouldDelete = PerformFrameworkValidation(xml, docIdCache);
                    if (!shouldDelete)
                    {
                        var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true };
                        using (var xw = XmlWriter.Create(file, xws))
                        {
                            try
                            {
                                xml.Save(xw);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                                Console.WriteLine("Target file: " + file);
                                Console.WriteLine("Contents of the failed file:");
                                Console.WriteLine(xml.ToString());
                            }
                        }
                    }
                    else
                    {
                        File.Delete(file);
                    }

                    Logger.InternalLog($"Individual members in the type: {elementSet.Count()}");
                }
            }
        }

        /// <summary>
        ///     Performs validation against the mdoc-generated framework files.
        /// </summary>
        private static bool PerformFrameworkValidation(XDocument doc, IEnumerable<string> cache)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            if (doc.Root == null) return false;
            var setOfDocIDs = from c in doc.Root.Descendants()
                where c.Attribute("Language") != null &&
                      ((string) c.Attribute("Language")).Equals("DocId", StringComparison.CurrentCultureIgnoreCase)
                select c;

            var elementsToRemove = new List<XElement>();

            foreach (var docIdElement in setOfDocIDs)
            {
                var targetLookupDocId = (string) docIdElement.Attribute("Value");

                if (cache.Contains(targetLookupDocId, StringComparer.InvariantCultureIgnoreCase))
                {
                    if (elementsToRemove.Contains(docIdElement))
                        elementsToRemove.Remove(docIdElement);
                }
                else
                {
                    elementsToRemove.Add(docIdElement);
                }
            }

            if (!elementsToRemove.Any()) return false;
            for (var i = elementsToRemove.Count - 1; i > -1; i--)
                if (elementsToRemove[i].Name.LocalName.Equals("Type", StringComparison.CurrentCultureIgnoreCase))
                    return true;
                else
                    elementsToRemove[i].Parent?.Remove();

            doc.Descendants()
                .Where(a => a.IsEmpty && string.IsNullOrWhiteSpace(a.Value) && !a.Attributes().Any())
                .Remove();

            return false;
        }

        private static void ProcessDupeContent(XDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var descendants = doc.Descendants("Docs");
            var contentAnalyzed = new HashSet<string>();

            // Keep track of nodes in which need to only keep one element.
            string[] unaryElements = {"summary"};

            // Each entity here is a <docs></docs> node that we need to validate.
            foreach (var el in descendants)
            {
                // Elements grouped by their type.
                var groupedElements = el.Elements().GroupBy(g => g.Name.LocalName.ToString());

                // Iterate through each group individually.
                foreach (var element in groupedElements)
                {
                    var sequenceCount = element.Count();
                    
                    if (sequenceCount > 1 && unaryElements.Contains(element.Key))
                    {
                        Console.WriteLine("Elements in " + element.Key + " sequence: " + sequenceCount);
                        // An element was detected that should be one, but is in several instances.

                        for (var i = 1; i < sequenceCount; i++)
                        {
                            // Skip first element, remove the rest.
                            var x = el.Elements(element.Key).Last();
                            x.Remove();
                        }

                        // For a given sequence, it's safe to assume that no other checks need to be done
                        // because we don't support dupe content in it anyway.
                        continue;
                    }

                    // This will iterate through each element in the group.
                    foreach (var partOfGroup in element)
                    {
                        var targetContent = partOfGroup.ToString().ToLower().Replace("  ", " ").Trim();

                        if (contentAnalyzed.Contains(targetContent))
                            continue;
                        contentAnalyzed.Add(targetContent);

                        var nodesMatching = from x in element
                            where x.ToString().Replace("  ", " ").Trim().Equals(targetContent,
                                StringComparison.CurrentCultureIgnoreCase)
                            select x;

                        // There are dupe elements within the same <docs></docs> node.
                        // These need to be removed.
                        if (nodesMatching.Count() <= 1) continue;
                        var matches = nodesMatching.Count();

                        for (var i = 0; i < matches - 1; i++)
                            el.Elements().First(x =>
                                x.ToString().Replace("  ", " ").Trim().Equals(targetContent,
                                    StringComparison.CurrentCultureIgnoreCase)).Remove();
                    }

                    contentAnalyzed.Clear();
                }
            }
        }
    }
}