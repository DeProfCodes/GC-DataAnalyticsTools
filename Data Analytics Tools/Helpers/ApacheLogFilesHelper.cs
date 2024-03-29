﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Data_Analytics_Tools.BusinessLogic;
using Data_Analytics_Tools.Constants;
using Data_Analytics_Tools.Hubs;
using Data_Analytics_Tools.Models.APIResponse;
using static System.Net.WebRequestMethods;

namespace Data_Analytics_Tools.Helpers
{
    public class ApacheLogFilesHelper
    {
        WebHelper api;
        SQL sql;
        ParquetFilesHelper parquets;
        IBusinessLogicData dataIO;
        WebClient webClient;
        Dictionary<string, List<Dictionary<string, string>>> schemas;
        AdminHub signalR;

        private string apacheLogsDirectory;
        private string apacheLogsDirectory_LogHashes;
        private string apacheLogsDirectory_Downloads;

        private string databaseName = "";

        public ApacheLogFilesHelper(IBusinessLogicData dataIO, AdminHub signalR)
        {
            api = new WebHelper();
            sql = new SQL();
            parquets = new ParquetFilesHelper();
            webClient = new WebClient();

            this.dataIO = dataIO;
            this.signalR = signalR;

            schemas = new Dictionary<string, List<Dictionary<string, string>>>();

            SetApacheLogsDirectory(@"F:\Proficient\DATA\Apache Log Files");
            //SetApacheLogsDirectory(@"F:\Proficient\DATA\Apache Log Files (VDC_DT)");
            databaseName = "Q1_2023";
            //databaseName = "Q1_2023_VDC_DT_TEST";
        }

        #region Tables Schema
        public static string GetTableName(string query)
        {
            if (query.ToLower().Contains("insert") || query.ToLower().Contains("create"))
            {
                string table = "";
                int idx = query.IndexOf("\"");
                char t = query[++idx];
                while (t != '\"')
                {
                    table += t;
                    t = query[++idx];
                }
                return table;
            }
            return null;
        }

        public string ExtractTables(StreamReader file, List<string> Queries, ref string line)
        {
            line = file.ReadLine();
            string query = "";
            string queryBuilder = "";

            var currTableName = "";
            var currTableSchema = new List<Dictionary<string, string>>();
            Dictionary<string, string> dict;

            while (!line.Contains("}"))
            {
                var ls = line.Split("\t");
                if (ls.Length == 3 && (ls[2].ToLower() == "int," || ls[2].ToLower() == "integer,"))
                {
                    line = ls[0] + "\t" + ls[1] + "\t" + "BIGINT,";  //force all int tables to be bigint. cause: log_hash (int) sqlite sometimes too big for mysql (int)
                }
                if (ls.Length == 2 || (ls.Length == 3 && (ls[2].Trim() == "" || ls[2].Trim() == "," || ls[2].Trim().Length == 1)))
                {
                    if (ls.Length == 3 && ls[2] == ",")
                    {
                        line = line.Replace(",", "");
                    }

                    if (!ls[0].Contains("PRIMARY") && !ls[1].Contains("PRIMARY"))
                        line += "TEXT,";
                }
                if (line.Contains("TIMESTAMP"))
                {
                    line = line.Replace("TIMESTAMP", "DATETIME");
                }
                if (line.Contains("BLOB"))
                {
                    line = line.Replace("BLOB", "TEXT");
                }
                if (line.Contains("NUM"))
                {
                    line = line.Replace("NUM", "BIGINT");
                }

                ls = line.Split("\t");
                if (ls.Length == 3)
                {
                    var dtp = ls[2].Replace(",", "");
                    var fld = ls[1].Replace("\"", "");
                    dict = new Dictionary<string, string>();
                    dict.Add(fld, dtp);
                    currTableSchema.Add(dict);
                }

                
                query += line + "\n";

                if (line == null || line.Contains("CREATE"))
                {
                    queryBuilder = queryBuilder.Replace("IF NOT EXISTS", "");
                    queryBuilder = queryBuilder.Replace("DEFAULT CURRENT_DATETIME", "");
                    queryBuilder = queryBuilder.Replace("AUTOINCREMENT", "");//
                    queryBuilder = queryBuilder.Replace("INTEGER_BOOLEAN", "BIT");
                    queryBuilder = queryBuilder.Replace("COMMIT;", "");

                    query = query.Replace("IF NOT EXISTS", "");
                    query = query.Replace("DEFAULT CURRENT_DATETIME", "");
                    query = query.Replace("AUTOINCREMENT", "");
                    query = query.Replace("INTEGER_BOOLEAN", "BIT");
                    query = query.Replace("COMMIT;", "");

                    Queries.Add(queryBuilder);

                    var q = line != null ? line : queryBuilder;
                    var tableName = GetTableName(q);

                    if (tableName != null)
                    {
                        if (currTableName != "")
                        {
                            if (!schemas.ContainsKey(currTableName))
                                schemas.Add(currTableName.ToLower(), currTableSchema);

                            currTableSchema = new List<Dictionary<string, string>>();
                        }
                        currTableName = tableName;
                    }

                    if (line == null)
                    {
                        break;
                    }
                    queryBuilder = "";
                }
                else 
                {
                    queryBuilder += line + "\n";
                }

                if (line.ToLower().Contains("insert"))
                {
                    if (currTableName != "" && currTableSchema.Count > 0)
                    {
                        if (!schemas.ContainsKey(currTableName))
                            schemas.Add(currTableName, currTableSchema);

                        currTableSchema = new List<Dictionary<string, string>>();
                    }
                    break;
                }

                line = file.ReadLine();
                if (line == null)
                    break;
            }

            if (currTableName != "" && currTableSchema.Count > 0)
            {
                if (!schemas.ContainsKey(currTableName))
                    schemas.Add(currTableName, currTableSchema);
            }

            query = query.Replace("IF NOT EXISTS", "");
            query = query.Replace("COMMIT;", "");

            return query;
        }

        public async Task CreateTablesSchema(bool schemaOnly = false)
        {
            await sql.CreateDatabase(databaseName);

            var schemaDir = "DATA\\Schema.txt";

            StreamReader file = new StreamReader(schemaDir);
            var createTablesQueries = new List<string>();

            string line = file.ReadLine();
            try
            {
                var createTables = ExtractTables(file, createTablesQueries, ref line);

                if (!schemaOnly)
                {
                    await sql.RunBulkQueries(createTablesQueries);
                    //await sql.RunQuery(createTables);
                }
            }
            catch (Exception e)
            {
                int t = 5;
            }
        }

        public void SetApacheLogsDirectory(string directory)
        {
            apacheLogsDirectory = directory;
            apacheLogsDirectory_LogHashes = apacheLogsDirectory + @"\Log hashes\";
            apacheLogsDirectory_Downloads = apacheLogsDirectory + @"\Downloads\";
        }
        #endregion

        #region Downloading Apache Logs
        public async Task CreateLogFileListForDownload(DateTime startDate, DateTime endDate)
        {
            string logHashesListFile = apacheLogsDirectory_LogHashes + "log_hash_keys.txt";
            string hashListFileForDownloadDir = apacheLogsDirectory_LogHashes + "log_fileList_for_download.txt";

            StreamReader hashesFile = new StreamReader(logHashesListFile);
            StreamWriter hashListFileForDownload = new StreamWriter(hashListFileForDownloadDir);

            string azenqosPrefix = "https://gnu0.azenqos.com/logs";
            var tables = ApacheConstants.GetApacheKnownTables();

            //var logHashes = (hashesFile.ReadToEnd()).Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries);

            var logHashes = (await api.ListAllParquets(startDate, endDate)).OrderBy(x => x.StartDate).ToList();

            hashListFileForDownload.Flush();
            foreach (var logHash in logHashes)
            {
                foreach (var table in tables)
                {
                    var downloadLine = $"{azenqosPrefix}/{logHash.StartDate.Year}_{logHash.StartDate.Month.ToString("D2")}/{table}_{logHash.LogHash}.parquet" + ",";
                    hashListFileForDownload.WriteLine(downloadLine);
                }
            }

            hashesFile.Close();
            hashListFileForDownload.Close();
        }

        private string GetApacheFileName(string apacheLink)
        {
            var data = apacheLink.Split("/");
            var fileName = data[data.Length - 1];

            return fileName;
        }

        private string GetApacheFileLogHash(string apacheLink)
        {
            var filename = GetApacheFileName(apacheLink);
            var data = filename.Split("_");

            var logHash = (data[data.Length - 1].Split("."))[0];

            return logHash;
        }

        private void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        private string CombineBrokenString(string[] data, int startIdx)
        {
            string output = "";
            for (int i = startIdx; i < data.Length; i++)
            {
                output += data[i];
            }
            return output;
        }

        private string GetCarOperatorPath(ApacheLogHash apacheLog)
        {
            var operatorCar = apacheLog.LogScriptName.Split("_");

            switch (operatorCar.Length)
            {
                default: return $"\\{operatorCar[0]}\\{operatorCar[1]}\\{CombineBrokenString(operatorCar, 2)}";
                case 2: return $"\\{operatorCar[0]}\\{operatorCar[1]}";
                case 1: return $"\\{operatorCar[0]}";
                case 0: return "";
            }
        }

        private string GetDestinationFolder(ApacheLogHash apacheLog)
        {
            
            var yearFolder = $"{apacheLogsDirectory_Downloads}\\{apacheLog.StartDate.Year}";
            var monthFolder = $"{yearFolder}\\{apacheLog.StartDate.ToString("MMMM")}";
            var logScriptNameFolder = $"{monthFolder}{GetCarOperatorPath(apacheLog)}";

            return logScriptNameFolder;
        }

        private void CreateFolders(ApacheLogHash apacheLog)
        {
            var yearFolder = $"{apacheLogsDirectory_Downloads}\\{apacheLog.StartDate.Year}";
            var monthFolder = $"{yearFolder}\\{apacheLog.StartDate.ToString("MMMM")}";

            CreateDirectory(yearFolder);
            CreateDirectory(monthFolder);

            var operatorCar = apacheLog.LogScriptName.Split("_");
            if (operatorCar.Length > 2)
            {
                var Operator = $"{monthFolder}\\{operatorCar[0]}";
                var carNum = $"{Operator}\\{operatorCar[1]}";
                var dbName = $"{carNum}\\{CombineBrokenString(operatorCar, 2)}";

                CreateDirectory(Operator);
                CreateDirectory(carNum);
                CreateDirectory(dbName);
            }
            else if (operatorCar.Length == 2)
            {
                var Operator = $"{monthFolder}\\{operatorCar[0]}";
                var carNum = $"{Operator}\\{operatorCar[1]}";
                
                CreateDirectory(Operator);
                CreateDirectory(carNum);
            }
            else if (operatorCar.Length == 1)
            {
                var Operator = $"{monthFolder}\\{operatorCar[0]}";
                
                CreateDirectory(Operator);
            }



        }

        private string GetServerFileName(ApacheLogHash apacheLog, string fileUrl)
        {
            var operatorCar = apacheLog.LogScriptName.Split("_");
            var apacheFileName = GetApacheFileName(fileUrl);
            var outName = $"{apacheLog.StartDate.ToString("dd_MM_yyyy")}-{parquets.GetTableNameFromApacheFilePath(apacheFileName)}.parquet";

            return outName;
        }

        private bool DownloadApacheFileFromServer(ApacheLogHash apacheLog, string fileUrl,string destination, List<string> errorsList)
        {
            try
            {
                CreateFolders(apacheLog);
                webClient.DownloadFile(fileUrl, destination);   
                return true;
            }
            catch (Exception e)
            {
                errorsList.Add(e.Message);
            }
            return false;
        }

        private bool DownloadApacheFileFromServerQuick(string fileUrl,List<string> errorsList)
        {
            try
            {
                var filename = GetApacheFileName(fileUrl);
                var destination = $"{apacheLogsDirectory_Downloads}\\QuickJan\\{filename}";
                webClient.DownloadFile(fileUrl, destination);
                return true;
            }
            catch (Exception e)
            {
                errorsList.Add(e.Message);
            }
            return false;
        }

        public async Task DownloadApacheFilesFromServerQuick(DateTime startDate, DateTime endDate)
        {
            //await CreateLogFileListForDownload(startDate, endDate);

            string logfileList = apacheLogsDirectory_LogHashes + "log_fileList_for_download.txt";
            StreamReader file = new StreamReader(logfileList);

            var allApacheLinks = file.ReadToEnd().Split(",");
            file.Close();

            var errorsList = new List<string>();

            var downlods = 0;
            foreach (var apacheLink in allApacheLinks)
            {
                var downloaded = DownloadApacheFileFromServerQuick(apacheLink, errorsList);
                if (downloaded)
                {
                    downlods++; 
                }
            }
        }
        public async Task DownloadApacheFilesFromServer(DateTime startDate, DateTime endDate)
        {
           // await api.ListAllParquets();

            string logfileList = apacheLogsDirectory_LogHashes + "log_fileList_for_download.txt";
            StreamReader file = new StreamReader(logfileList);

            var allApacheLinks = file.ReadToEnd().Split(",");
            file.Close();

            var errorsList = new List<string>();

            var logHashes = await api.ListAllParquets(startDate, endDate);

            var lastDate = new DateTime();
            var downloadsCount = 0;

            foreach (var apacheLink in allApacheLinks)
            {
                try
                {
                    var logId = GetApacheFileLogHash(apacheLink);
                    var logHash = long.Parse(logId);
                    var apacheLog = logHashes.FirstOrDefault(x => x.LogHash == logHash);
                    if (apacheLog != null)
                    {
                        var destination = $"{GetDestinationFolder(apacheLog)}\\{GetServerFileName(apacheLog, apacheLink)}";

                        var date = apacheLog.StartDate;

                        if (date.Day != lastDate.Day || date.Month != lastDate.Month || date.Year != lastDate.Year)
                        {
                            var dateMsg = $"Apache Logs for date : <b>{date.ToString("dd/MM/yyyy")}</b>";
                            await signalR.SendApacheLogsProcess("date", dateMsg, "started");
                            lastDate = date;
                        }
                        var apacheFileServer = GetServerFileName(apacheLog, apacheLink);
                        var message = $"Download:'<b>{apacheLog.LogTag}_{apacheFileServer}</b>'...";

                        await signalR.SendApacheLogsProcess("download", message, "started");

                        bool downloaded = DownloadApacheFileFromServer(apacheLog, apacheLink, destination, errorsList);

                        if (downloaded)
                        {
                            downloadsCount++;
                            await signalR.SendApacheLogsProcess("import", $"'{apacheLog.LogTag}_{apacheFileServer}' downloaded!", "completed");
                            await signalR.SendApacheLogsProcess("downloadsCount", $"{downloadsCount}", "completed");
                        }
                    }
                }
                catch (Exception e)
                {
                    await signalR.SendApacheLogsProcess("import", $"error: '{e.Message}' ", "error");
                }
            }
        }

        private string GetFileNameFromFilePath(string filePath)
        {
            var data = filePath.Split("\\");

            return data[data.Length - 1];
        }
        #endregion

        #region Apache Files To MySQL

        public async Task<int> ImportApacheFileToMySQL(string filePath, List<string> errors)
        {
            var fileName = GetFileNameFromFilePath(filePath);
            var tableName = NameExtractionHelpers.GetTableNameFromApacheFile(fileName); 
            try
            {
                var insertQueries = await parquets.CreateMySQLInsertQueries(filePath, schemas);
                int rows = await sql.RunQuery(insertQueries, tableName);

                if (rows > 0)
                {
                    await dataIO.AddOrUpdateApacheLogFileImport(filePath, fileName, true, "");
                }
                else
                {
                    await dataIO.AddOrUpdateApacheLogFileImport(filePath, fileName, false, "");
                }
                return rows;
            }
            catch (Exception e)
            {
                await dataIO.AddOrUpdateApacheLogFileImport(filePath, fileName, false, e.Message);
                errors.Add(e.Message);
            }
            return -1;
        }

        public async Task ImportApacheFilesToMySQL()
        {
            var location = apacheLogsDirectory_Downloads;

            var apacheFiles = Directory.GetFiles(location);
            
            int successCount = 0;

            var errors = new List<string>();
            
            foreach (var afile in apacheFiles)
            {
                int rows = await ImportApacheFileToMySQL(afile, errors);

                successCount += rows > 0 ? 1 : 0;
            }
        }

        public async Task DownloadAndImportApacheFilesToMySQL(DateTime startDate, DateTime endDate)
        {
            string logfileList = apacheLogsDirectory_LogHashes + "log_fileList_for_download.txt";
            StreamReader file = new StreamReader(logfileList);

            var allApacheLinks = file.ReadToEnd().Split(",");
            file.Close();

            int downloadsCount = 0;
            int importedFilesCount = 0;

            var errorsList = new List<string>();

            var logHashes = (await api.ListAllParquets(startDate, endDate)).OrderBy(x=>x.StartDate).ToList();

            //var test = logHashes.Where(x => x.LogScriptName == "Vodacom_Car1_UMTS_Data").ToList();

            var lastDate = new DateTime();
            
            var processFiles = await dataIO.GetProcessedApacheFiles2();
            importedFilesCount = processFiles.Count;

            foreach (var apacheLink in allApacheLinks)
            {
                try
                {
                    var logHash = long.Parse(GetApacheFileLogHash(apacheLink));
                    var apacheLog = logHashes.FirstOrDefault(x => x.LogHash == logHash);

                    var apacheFileServer = GetServerFileName(apacheLog, apacheLink);
                    var localApacheFileDir = $"{GetDestinationFolder(apacheLog)}\\{apacheFileServer}";
                    //var apacheFileNameServer = $"{apacheLogsDirectory_Downloads}" + "\\" + apacheFileName;

                    var procsdFile = processFiles.FirstOrDefault(x => x.FilePath == localApacheFileDir);

                    if (procsdFile != null && procsdFile.ImportComplete)
                    {
                        continue;
                    }

                    var date = apacheLog.StartDate;

                    if (date.Day != lastDate.Day || date.Month != lastDate.Month || date.Year != lastDate.Year)
                    {
                        var dateMsg = $"Apache Logs for date : <b>{date.ToString("dd/MM/yyyy")}</b>";
                        await signalR.SendApacheLogsProcess("date", dateMsg, "started");
                        lastDate = date;
                    }

                    var message = $"Download:'<b>{apacheLog.LogTag}_{apacheFileServer}</b>'...";

                    await signalR.SendApacheLogsProcess("download", message, "started");

                    bool downloaded = DownloadApacheFileFromServer(apacheLog, apacheLink, localApacheFileDir, errorsList);

                    if (downloaded)
                    {
                        downloadsCount++;
                        await signalR.SendApacheLogsProcess("download", $"'{apacheLog.LogTag}_{apacheFileServer}' downloaded!", "completed");
                        await signalR.SendApacheLogsProcess("downloadsCount", $"{downloadsCount}", "completed");

                        message = $"Import:'<b>{apacheLog.LogTag}_{apacheFileServer}</b>' to MySQL Db = <b>{databaseName}</b>...";

                        await signalR.SendApacheLogsProcess("import", message, "started");

                        int rows = await ImportApacheFileToMySQL(localApacheFileDir, errorsList);

                        if (rows > 0)
                        {
                            importedFilesCount++;
                            await signalR.SendApacheLogsProcess("import", $"'{apacheLog.LogTag}_{apacheFileServer}' imported", "completed");
                            await signalR.SendApacheLogsProcess("importsCount", $"{importedFilesCount}", "completed");
                        }

                    }
                }
                catch (Exception e)
                {
                    errorsList.Add(e.Message);
                }
            }
        }


        #endregion
    }
}
