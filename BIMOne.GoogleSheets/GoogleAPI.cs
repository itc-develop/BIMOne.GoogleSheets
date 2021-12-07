﻿using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Reflection;
using Autodesk.DesignScript.Runtime;
using System.Threading.Tasks;

namespace BIMOne
{
    public static class GoogleAPI
    {
        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/sheets.googleapis.com-dotnet-quickstart.json
        static string[] Scopes = { SheetsService.Scope.Drive, SheetsService.Scope.Spreadsheets };
        static string ApplicationName = "BIM One Google Sheets";

        // Get relative path from DLL to credentials file
        static string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        static string credentialsPath = Path.Combine(assemblyPath, @"..\extra\credentials.json");

        static IConfigurableHttpClientInitializer credential;

        static DriveService driveService { get => 
            // Create Google Drive API service.
            new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }
        static SheetsService sheetsService { get => 
            // Create Google Sheets API service.
            new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        static void checkForCredentials()
        {
            if (!File.Exists(credentialsPath))
            {
                throw new FileNotFoundException(String.Format("credentials.json not found at path {0}. Make sure to place it in the Dynamo packages path under BIMOneGoogleAPI\\extra\\credentials.json", credentialsPath));
            }
        }

        [IsVisibleInDynamoLibrary(false)]
        static GoogleAPI()
        {
            // No longer getting credentials here since it seems impossible to get a useful error message
            // back up to Dynamo from the Class Initializer.
            // GetCredentials();
        }

        [IsVisibleInDynamoLibrary(false)]
        static IConfigurableHttpClientInitializer GetCredentials()
        {
            checkForCredentials();

            try
            {
                using (var stream =
                    new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                {
                    return credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None).Result;
                }
            }
            catch
            {
                using (var stream = new FileStream(credentialsPath,
                    FileMode.Open, FileAccess.Read))
                {
                    return credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
                }
            }
        }

        /// <summary>
        /// Resets the credentials. Useful when we add new features that require additional permission scopes.
        /// </summary>
        /// <returns>bool</returns>
        /// <search>
        /// </search>
        public async static Task<bool> Logout()
        {
            var credential = GetCredentials();
            if (credential.GetType() == typeof(UserCredential))
            {
                return await ((UserCredential) credential).RevokeTokenAsync(CancellationToken.None);
            }

            return true;
        }

        /// <summary>
        /// A useless node, DO NOT USE.
        /// </summary>
        /// <returns>nothing</returns>
        /// <search>
        /// </search>
        [IsVisibleInDynamoLibrary(true)]
        public static void About() { }

        /// <summary>
        /// Gets a list of Google Sheets present in a user's Google Drive™ with optional
        /// 'contains' keyword filter.
        /// </summary>
        /// <param name="filter">The text filter to use when searching for Google Sheets on the Drive.</param>
        /// <param name="corpora">The corpora determines de scope to search in. 
        /// Options are: 'user' (files created by, opened by, or shared directly with the user), 'drive' 
        /// (files in the specified shared drive as indicated by the 'driveId'), 'domain' 
        /// (files shared to the user's domain), and 'allDrives' (A combination of 'user' and 'drive' for all drives where the user is a member). 
        /// When able, use 'user' or 'drive', instead of 'allDrives', for efficiency.
        /// Defaults to "allDrives" for simplicity.</param>
        /// <returns>fileNames, fileIds as lists.</returns>
        /// <search>
        /// google, sheets, drive, read
        /// </search>
        [MultiReturn(new[] { "fileNames", "fileIds" })]
        public static Dictionary<string, object> GetGoogleSheetsInGoogleDrive(string filter = "", string corpora = "allDrives")
        {
            GetCredentials();

            var fileNames = new List<string>();
            var fileIds= new List<string>();

            // Get all spreadsheets from drive
            // Define parameters of request.
            FilesResource.ListRequest listRequest = driveService.Files.List();
            listRequest.PageSize = 1000;
            listRequest.Fields = "nextPageToken, files(id, name)";
            listRequest.Corpora = corpora;
            if (corpora == "allDrives" || corpora == "drive")
            {
                listRequest.IncludeItemsFromAllDrives = true;
                listRequest.SupportsAllDrives = true;
            }
            listRequest.Q = String.Format("mimeType='application/vnd.google-apps.spreadsheet' and name contains '{0}'", filter);
            listRequest.OrderBy = "name";

            // List files.
            IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute()
                .Files;

            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    fileNames.Add(file.Name);
                    fileIds.Add(file.Id);
                }
            }
            else
            {
                fileNames.Add("No sheets found");
                fileIds.Add("No sheets found");
            }

            var d = new Dictionary<string, object>();
            d.Add("fileNames", fileNames);
            d.Add("fileIds", fileIds);
            return d;
        }

        /// <summary>
        /// Copy a Google Sheet.
        /// </summary>
        /// <param name="fileId">The unique ID of the Google Sheet to copy.</param>
        /// <returns>fileName, fileId.</returns>
        /// <search>
        /// google, sheets, drive, copy
        /// </search>
        [MultiReturn(new[] { "fileName", "fileId" })]
        public static Dictionary<string, object> CopyGoogleSheet(string fileId)
        {
            GetCredentials();

            Google.Apis.Drive.v3.Data.File copiedFile = new Google.Apis.Drive.v3.Data.File();

            // Copy Google Sheet
            // Define parameters of request.
            FilesResource.CopyRequest copyRequest = driveService.Files.Copy(copiedFile, fileId);
            copyRequest.SupportsAllDrives = true;

            Google.Apis.Drive.v3.Data.File file = copyRequest.Execute();

            var d = new Dictionary<string, object>();
            d.Add("fileName", file.Name);
            d.Add("fileId", file.Id);
            return d;
        }

        /// <summary>
        /// Appends a nested list of lists to a Google Sheet™. The first table detected in the provided range will be the one that the API
        /// appends the new data to.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="sheet">The name of the sheet within the spreadsheet as string. Ex.: Sheet1 </param>
        /// <param name="range">The range where to try and find a table, as string. Ex.: A:Z</param>
        /// <param name="data">A list of lists containing the data to append to the table in Google Sheets.</param>
        /// <param name="userInputModeRaw">If true, prevents Google sheets from auto-detecting the formatting of the data (useful for dates).</param>
        /// <param name="includeValuesInResponse">If true, returns the updated/appended data in the response.</param>
        /// <returns>"spreadsheetID", "updatedValues", "range"</returns>
        /// <search>
        /// google, sheets, drive, write
        /// </search>
        [MultiReturn(new[] { "spreadsheetID", "updatedValues", "range" })]
        public static Dictionary<string, object> AppendDataToGoogleSheetTable(string spreadsheetId, string sheet, string range, List<IList<object>> data, bool userInputModeRaw = false, bool includeValuesInResponse = false)
        {
            GetCredentials();

            range = formatRange(sheet, range);

            var valueRange = new ValueRange();
            valueRange.Values = data;

            var appendRequest = sheetsService.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
            appendRequest.IncludeValuesInResponse = includeValuesInResponse;

            if (userInputModeRaw)
            {
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            }
            else
            {
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            }

            var appendResponse = appendRequest.Execute();

            var d = new Dictionary<string, object>();
            d.Add("spreadsheetID", appendResponse.SpreadsheetId);

            if (includeValuesInResponse)
            {
                var updatedValues = appendResponse.Updates.UpdatedData;
                d.Add("updatedValues", updatedValues.Values);
                d.Add("range", updatedValues.Range);
            }
            else
            {
                d.Add("updatedValues", "");
                d.Add("range", "");
            }

            return d;
        }

        /// <summary>
        /// Appends a nested list of lists to a Google Sheet™. The first table detected in the provided range will be the one that the API
        /// appends the new data to.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="sheets">The names of the sheets within the spreadsheet as a list of strings. Ex.: Sheet1 </param>
        /// <param name="data">A list of lists of lists containing the data to append to the table in Google Sheets. Outter list corresponds to sheets, 
        /// first inner to rows and innermost to columns within the rows</param>
        /// <param name="userInputModeRaw">If true, prevents Google sheets from auto-detecting the formatting of the data (useful for dates).</param>
        /// <param name="includeValuesInResponse">If true, returns the updated/appended data in the response.</param>
        /// <returns>"spreadsheetID", "updatedValues", "range"</returns>
        /// <search>
        /// google, sheets, drive, write
        /// </search>
        [MultiReturn(new[] { "spreadsheetID", "replies" })]
        public static Dictionary<string, object> BatchAppendDataToGoogleSheet(string spreadsheetId, List<string> sheets, List<List<IList<object>>>data, bool userInputModeRaw = false, bool includeValuesInResponse = false)
        {
            if (sheets.Count != data.Count)
            {
                // Raise a problem because the input length of the sheets and data list must match.
                throw new Exception("input length of the sheets and data list must match");
            }

            GetCredentials();

            List<int> sheetIds = lookupSheetIds(spreadsheetId, sheets);

            BatchUpdateSpreadsheetRequest requestBody = new BatchUpdateSpreadsheetRequest();
            requestBody.Requests = new List<Request>();

            for (int i = 0; i < sheets.Count; i++)
            {
                string sheet = sheets[i];
                List<IList<object>> sheetData = data[i];

                AppendCellsRequest appendCellsRequest = new AppendCellsRequest();
                appendCellsRequest.SheetId = sheetIds[i];

                List<RowData> rowDatas = new List<RowData>();
                foreach (var row in sheetData)
                {
                    RowData rowData = new RowData();
                    List<CellData> cellDatas = new List<CellData>();

                    foreach (var column in row)
                    {
                        CellData cellData = new CellData();
                        ExtendedValue extendedValue = new ExtendedValue();
                        // Detect the value type (number, bool, string, formula) and parse accordingly
                        var isFormula = column.ToString().StartsWith("=");
                        var isNumeric = IsNumeric(column);
                        var isBool = bool.TryParse(column.ToString(), out bool y);

                        if (isFormula)
                        {
                            // Send as formula
                            extendedValue.FormulaValue = column.ToString();
                        }
                        else if (isNumeric)
                        {
                            // Send as number
                            extendedValue.NumberValue = double.Parse(column.ToString());
                        }
                        else if (isBool)
                        {
                            // Send as bool
                            extendedValue.BoolValue = y;
                        }
                        else
                        {
                            // Default to string
                            extendedValue.StringValue = column.ToString();
                        }

                        cellData.UserEnteredValue = extendedValue;

                        cellDatas.Add(cellData);

                    }
                    rowData.Values = cellDatas;
                    rowDatas.Add(rowData);
                }
                appendCellsRequest.Fields = "*";
                appendCellsRequest.Rows = rowDatas;

                requestBody.Requests.Add(new Request
                {
                    AppendCells = appendCellsRequest
                });
            }

            // TODO: Range no longer does anything, but we need to allow the user to pick the starting point for the append.
            //range = formatRange(sheet, range);

            SpreadsheetsResource.BatchUpdateRequest batchRequest = sheetsService.Spreadsheets.BatchUpdate(requestBody, spreadsheetId);
            BatchUpdateSpreadsheetResponse response = batchRequest.Execute();
 
            var d = new Dictionary<string, object>();
            d.Add("spreadsheetID", response.SpreadsheetId);

            if (includeValuesInResponse)
            {
                d.Add("replies", response.Replies);
            }
            else
            {
                d.Add("replies", "");
            }

            return d;
        }

        /// <summary>
        /// Writes a nested list of lists to a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="sheet">The name of the sheet within the spreadsheet as string. Ex.: Sheet1 </param>
        /// <param name="range">The range where to write the data as string. Ex.: A:Z</param>
        /// <param name="data">A list of lists containing the data to write to Google Sheets.</param>
        /// <param name="userInputModeRaw">If true, prevents Google sheets from auto-detecting the formatting of the data (useful for dates).</param>
        /// <param name="includeValuesInResponse">If true, returns the updated/appended data in the response.</param>
        /// <returns>"spreadsheetID", "updatedValues", "range"</returns>
        /// <search>
        /// google, sheets, drive, write
        /// </search>
        [MultiReturn(new[] { "spreadsheetID", "updatedValues", "range" })]
        public static Dictionary<string, object> WriteDataToGoogleSheet(string spreadsheetId, string sheet, string range, List<IList<object>> data, bool userInputModeRaw = false, bool includeValuesInResponse = false)
        {
            GetCredentials();

            range = formatRange(sheet, range);

            var valueRange = new ValueRange();
            valueRange.Values = data;

            SpreadsheetsResource.GetRequest getRequest = sheetsService.Spreadsheets.Get(spreadsheetId);
            var response = getRequest.Execute();
            var sheets = response.Sheets;

            bool sheetExists = false;
            foreach (var item in sheets)
            {
                if(item.Properties.Title == sheet)
                {
                    sheetExists = true;
                    break;
                }
            }

            if (!sheetExists)
            {
                var createSheetResponse = CreateNewSheetWithinGoogleSheet(spreadsheetId, sheet);
            }

            var updateRequest = sheetsService.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
            updateRequest.IncludeValuesInResponse = includeValuesInResponse;

            if (userInputModeRaw)
            {
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            }
            else
            {
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            }
            
            var updateResponse = updateRequest.Execute();

            var d = new Dictionary<string, object>();
            d.Add("spreadsheetID", updateResponse.SpreadsheetId);
            if (includeValuesInResponse)
            {
                var updatedValues = updateResponse.UpdatedData;
                d.Add("updatedValues", updatedValues.Values);
            }
            else
            {
                d.Add("updatedValues", "");
            }
            d.Add("range", updateResponse.UpdatedRange);

            return d;
        }

        /// <summary>
        /// Reads a specified range of a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="sheet">The name of the sheet within the spreadsheet as string. Ex.: Sheet1 </param>
        /// <param name="range">The range where to read the data from as string. Ex.: A:Z</param>
        /// <param name="unformattedValues">If true, reads Google sheets as raw values.</param>
        /// <returns>data</returns>
        /// <search>
        /// google, sheets, drive, read
        /// </search>
        [MultiReturn(new[] { "data" })]
        public static Dictionary<string, object> ReadGoogleSheet(string spreadsheetId, string sheet, string range, bool unformattedValues = false)
        {
            GetCredentials();

            range = formatRange(sheet, range);

            // How values should be represented in the output.
            // The default render option is ValueRenderOption.FORMATTED_VALUE.
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum valueRenderOption;
            if (unformattedValues)
            {
                valueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
            }
            else
            {
                valueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
            }

            // How dates, times, and durations should be represented in the output.
            // This is ignored if value_render_option is
            // FORMATTED_VALUE.
            // The default dateTime render option is [DateTimeRenderOption.SERIAL_NUMBER].
            SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum dateTimeRenderOption = 
                SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.FORMATTEDSTRING; 

            SpreadsheetsResource.ValuesResource.GetRequest request = sheetsService.Spreadsheets.Values.Get(spreadsheetId, range);
            request.ValueRenderOption = valueRenderOption;
            request.DateTimeRenderOption = dateTimeRenderOption;

            var getResponse = request.Execute();

            var d = new Dictionary<string, object>();
            d.Add("data", getResponse.Values);
            return d;
        }

        /// <summary>
        /// Reads multiple specified ranges of a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="ranges">A list of ranges where to read the data from as string. Ex.: Sheet1!A:Z, Sheet2!A:Z, ...</param>
        /// <param name="unformattedValues">If true, reads Google sheets as raw values.</param>
        /// <returns>data</returns>
        /// <search>
        /// google, sheets, drive, read
        /// </search>
        [MultiReturn(new[] { "ranges", "values" })]
        public static Dictionary<string, object> ReadGoogleSheetMultipleRanges(string spreadsheetId, List<string> ranges, bool unformattedValues = false)
        {
            GetCredentials();

            if (ranges == null || ranges.Count < 1)
            {
                // Default to all sheets
                SpreadsheetsResource.GetRequest sheetRequest = sheetsService.Spreadsheets.Get(spreadsheetId);
                var response = sheetRequest.Execute();

                var sheets = response.Sheets;

                foreach (Sheet sheet in sheets)
                {
                    ranges.Add(sheet.Properties.Title);
                }
            }
            // How values should be represented in the output.
            // The default render option is ValueRenderOption.FORMATTED_VALUE.
            SpreadsheetsResource.ValuesResource.BatchGetRequest.ValueRenderOptionEnum valueRenderOption;
            if (unformattedValues)
            {
                valueRenderOption = SpreadsheetsResource.ValuesResource.BatchGetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
            }
            else
            {
                valueRenderOption = SpreadsheetsResource.ValuesResource.BatchGetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
            }

            // How dates, times, and durations should be represented in the output.
            // This is ignored if value_render_option is
            // FORMATTED_VALUE.
            // The default dateTime render option is [DateTimeRenderOption.SERIAL_NUMBER].
            SpreadsheetsResource.ValuesResource.BatchGetRequest.DateTimeRenderOptionEnum dateTimeRenderOption =
                SpreadsheetsResource.ValuesResource.BatchGetRequest.DateTimeRenderOptionEnum.FORMATTEDSTRING;

            SpreadsheetsResource.ValuesResource.BatchGetRequest request = sheetsService.Spreadsheets.Values.BatchGet(spreadsheetId);
            request.Ranges = ranges;
            request.ValueRenderOption = valueRenderOption;
            request.DateTimeRenderOption = dateTimeRenderOption;

            BatchGetValuesResponse getResponse = request.Execute();

            var d = new Dictionary<string, object>();
            List<object> values = new List<object>();
            List<object> returnedRanges = new List<object>();

            foreach (var item in getResponse.ValueRanges)
            {
                values.Add(item.Values);
                returnedRanges.Add(item.Range);
            }
            d.Add("ranges", returnedRanges);
            d.Add("values", values);
            return d;
        }

        /// <summary>
        /// Clears values in the specified range of cells in a Google Sheet™. Only values, not formatting.
        /// </summary>
        /// <param name="spreadsheetId">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="sheet">The name of the sheet within the spreadsheet as string. Ex.: Sheet1 </param>
        /// <param name="range">The range where to write the data as string. Ex.: A:Z</param>
        /// <param name="search">An optional search string (if the cell "contains" that value it's a match). If present, all rows that contain the search string will be erased.</param>
        /// <returns>clearedRange</returns>
        /// <search>
        /// google, sheets, clear, range
        /// </search>
        [MultiReturn(new[] { "clearedRange" })]
        public static Dictionary<string, object> ClearValuesInRangeGoogleSheet(string spreadsheetId, string sheet, string range, string search = "")
        {
            GetCredentials();

            // Range format: SHEET:!A:F
            range = $"{sheet}!{range}";
            var d = new Dictionary<string, object>();

            if (search.Length != 0)
            {
                int sheetId = lookupSheetId(spreadsheetId, sheet);
                // Search within the range and if something matches, erase all rows that match.
                SpreadsheetsResource.ValuesResource.GetRequest getRequest = sheetsService.Spreadsheets.Values.Get(spreadsheetId, range);
                SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum valueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
                getRequest.ValueRenderOption = valueRenderOption;
                SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum dateTimeRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.FORMATTEDSTRING;
                getRequest.DateTimeRenderOption = dateTimeRenderOption;

                var getResponse = getRequest.Execute();

                var rows = getResponse.Values;
                List<int> rowsToDelete = new List<int>();

                for (int row = 0; row < rows.Count; row++)
                {
                    for (int column = 0; column < rows[row].Count; column++)
                    {
                        var cell = rows[row][column].ToString();
                        if (cell.Contains(search))
                        {
                            // We've found a match. 
                            rowsToDelete.Add(row);
                        }
                    }
                }

                if (rowsToDelete.Count > 0)
                {
                    List<string> ranges = new List<string>();
                    var requestBody = new BatchUpdateSpreadsheetRequest();
                    var requests = new List<Request>();

                    int rowOffset = 0;
                    foreach (int rowId in rowsToDelete)
                    {
                        DeleteRangeRequest deleteRangeRequest = new DeleteRangeRequest();
                        deleteRangeRequest.ShiftDimension = "ROWS";
                        GridRange gridRangeToDelete = new GridRange();
                        gridRangeToDelete.SheetId = sheetId;
                        gridRangeToDelete.StartRowIndex = rowId - rowOffset;
                        gridRangeToDelete.EndRowIndex = (rowId + 1) - rowOffset;
                        deleteRangeRequest.Range = gridRangeToDelete;

                        Request _request = new Request();
                        _request.DeleteRange = deleteRangeRequest;
                        
                        requests.Add(_request);
                        rowOffset++;
                    }

                    requestBody.Requests = requests;

                    SpreadsheetsResource.BatchUpdateRequest request = sheetsService.Spreadsheets.BatchUpdate(requestBody,spreadsheetId);

                    var clearResponse = request.Execute();
                    d.Add("clearedRange", clearResponse.Replies);
                }
                else
                {
                    d.Add("clearedRange", "Nothing matching search parameter was found.");
                }
            }
            else
            {
                // Just clear everything in that range
                var requestBody = new ClearValuesRequest();
                SpreadsheetsResource.ValuesResource.ClearRequest request = sheetsService.Spreadsheets.Values.Clear(requestBody, spreadsheetId, range);
                var clearResponse = request.Execute();
                d.Add("clearedRange", clearResponse.ClearedRange);
            }
            
            return d;
        }

        /// <summary>
        /// Gets sheet title and ids in a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetID">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <returns>sheetTitles</returns>
        /// <returns>sheetIds</returns>
        /// <search>
        /// google, sheets, titles, ids
        /// </search>
        [MultiReturn(new[] { "sheetTitles", "sheetIds" })]
        public static Dictionary<string, object> GetSheetsInGoogleSheet(string spreadsheetID)
        {
            GetCredentials();

            SpreadsheetsResource.GetRequest request = sheetsService.Spreadsheets.Get(spreadsheetID);
            var response = request.Execute();

            var sheets = response.Sheets;
            List<string> sheetTitles = new List<string>();
            List<string> sheetIds = new List<string>();
            foreach (Sheet sheet in sheets)
            {
                sheetTitles.Add(sheet.Properties.Title);
                sheetIds.Add(sheet.Properties.SheetId.ToString());
            }

            var d = new Dictionary<string, object>();
            d.Add("sheetTitles", sheetTitles);
            d.Add("sheetIds", sheetIds);
            return d;
        }

        /// <summary>
        /// Create a new sheet within a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetID">The ID of the Spreadsheet (long unique identifier as string)</param>
        /// <param name="newSheetTitle">The title of the new sheet</param>
        /// <returns>sheetTitle</returns>
        /// <returns>spreadsheetId</returns>
        /// <search>
        /// google, sheets, titles, ids, create
        /// </search>
        [MultiReturn(new[] { "sheetTitle", "spreadsheetId" })]
        public static Dictionary<string, object> CreateNewSheetWithinGoogleSheet(string spreadsheetID, string newSheetTitle)
        {
            GetCredentials();

            AddSheetRequest addSheetRequest = new AddSheetRequest();
            addSheetRequest.Properties = new SheetProperties();
            addSheetRequest.Properties.Title = newSheetTitle;

            BatchUpdateSpreadsheetRequest requestBody = new BatchUpdateSpreadsheetRequest();
            List<Request> requests = new List<Request>();
            requestBody.Requests = requests;
            requestBody.Requests.Add(new Request
            {
                AddSheet = addSheetRequest
            });

            SpreadsheetsResource.BatchUpdateRequest batchRequest = sheetsService.Spreadsheets.BatchUpdate(requestBody, spreadsheetID);

            BatchUpdateSpreadsheetResponse response = batchRequest.Execute();

            var d = new Dictionary<string, object>();
            d.Add("sheetTitle", newSheetTitle);
            d.Add("spreadsheetId", response.SpreadsheetId);
            return d;
        }

        /// <summary>
        /// Create a new Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetTitle">The title of the Spreadsheet</param>
        /// <param name="openInBrowser">Whether or not to open in browser afer successful creation.</param>
        /// <returns>spreadsheetId</returns>
        /// <returns>sheetUrl</returns>
        /// <search>
        /// google, sheets, title, create, spreadsheet
        /// </search>
        [MultiReturn(new[] { "spreadsheetId", "sheetUrl" })]
        public static Dictionary<string, object> CreateNewGoogleSheet(string spreadsheetTitle, bool openInBrowser = false)
        {
            GetCredentials();

            var spreadsheet = new Spreadsheet();

            spreadsheet.Properties = new SpreadsheetProperties();
            spreadsheet.Properties.Title = spreadsheetTitle;

            SpreadsheetsResource.CreateRequest request = sheetsService.Spreadsheets.Create(spreadsheet);

            var response = request.Execute();

            if(openInBrowser)
            {
                openLinkInBrowser(response.SpreadsheetUrl);
            }
            
            var d = new Dictionary<string, object>();
            d.Add("spreadsheetId", response.SpreadsheetId);
            d.Add("sheetUrl", response.SpreadsheetUrl);
            return d;
        }

        /// <summary>
        /// Delete a sheet by id within a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetID">The id of the Spreadsheet</param>
        /// <param name="sheetId">The id of the sheet within the Spreadsheet</param>
        /// <returns>spreadsheetId</returns>
        /// <search>
        /// google, sheets, title, delete, sheet
        /// </search>
        public static Dictionary<string, object> DeleteSheetByIdWithinGoogleSheet(string spreadsheetID, int sheetId)
        {
            GetCredentials();

            DeleteSheetRequest deleteSheetRequest = new DeleteSheetRequest();
            deleteSheetRequest.SheetId = sheetId;

            BatchUpdateSpreadsheetRequest requestBody = new BatchUpdateSpreadsheetRequest();
            List<Request> requests = new List<Request>();
            requestBody.Requests = requests;
            requestBody.Requests.Add(new Request
            {
                DeleteSheet = deleteSheetRequest
            });

            SpreadsheetsResource.BatchUpdateRequest batchRequest = sheetsService.Spreadsheets.BatchUpdate(requestBody, spreadsheetID);

            BatchUpdateSpreadsheetResponse response = batchRequest.Execute();

            var d = new Dictionary<string, object>();
            d.Add("spreadsheetId", response.SpreadsheetId);
            return d;
        }

        /// <summary>
        /// Delete a sheet by title within a Google Sheet™.
        /// </summary>
        /// <param name="spreadsheetID">The id of the Spreadsheet</param>
        /// <param name="sheetTitle">The title of the sheet within the Spreadsheet</param>
        /// <returns>spreadsheetId</returns>
        /// <search>
        /// google, sheets, title, delete, sheet
        /// </search>
        public static Dictionary<string, object> DeleteSheetByTitleWithinGoogleSheet(string spreadsheetID, string sheetTitle)
        {
            GetCredentials();

            int sheetId = lookupSheetId(spreadsheetID, sheetTitle);
            var d = new Dictionary<string, object>();
            if (sheetId > 0)
            {
                DeleteSheetRequest deleteSheetRequest = new DeleteSheetRequest();
                deleteSheetRequest.SheetId = sheetId;

                BatchUpdateSpreadsheetRequest requestBody = new BatchUpdateSpreadsheetRequest();
                List<Request> requests = new List<Request>();
                requestBody.Requests = requests;
                requestBody.Requests.Add(new Request
                {
                    DeleteSheet = deleteSheetRequest
                });

                SpreadsheetsResource.BatchUpdateRequest batchRequest = sheetsService.Spreadsheets.BatchUpdate(requestBody, spreadsheetID);

                BatchUpdateSpreadsheetResponse response = batchRequest.Execute();

                d.Add("spreadsheetId", response.SpreadsheetId);
            } 
            else
            {
                d.Add("response", "Sheet not found.");
            }
            return d;
        }

        [IsVisibleInDynamoLibrary(false)]
        static void openLinkInBrowser(string url)
        {
            System.Diagnostics.Process.Start(url);
        }

        [IsVisibleInDynamoLibrary(false)]
        static string formatRange(string sheet, string range)
        {
            // Range format: SHEET:!A:F
            if (range == "")
            {
                // Default to columns A through ZZ
                return $"{sheet}!A:ZZ";
            }
            else
            {
                return $"{sheet}!{range}";
            }
        }

        [IsVisibleInDynamoLibrary(false)]
        static int lookupSheetId(string spreadsheetId, string sheetTitle)
        {
            Dictionary<string, object> lookup = GetSheetsInGoogleSheet(spreadsheetId);
            
            if (lookup.ContainsKey("sheetTitles"))
            {
                var values = new object();
                lookup.TryGetValue("sheetTitles", out values);

                List<string> sheetTitles = (List<string>)values;
                int sheetIndex = -1;
                for (int i = 0; i < sheetTitles.Count; i++)
                {
                    if (sheetTitles[i] == sheetTitle)
                    {
                        sheetIndex = i;
                    }
                }

                values = new object();
                lookup.TryGetValue("sheetIds", out values);
                List<string> sheetIds = (List<string>)values;

                int sheetId = Int32.Parse(sheetIds[sheetIndex]);

                return sheetId;
            }
            else
            {
                return 0;
            }
        }

        [IsVisibleInDynamoLibrary(false)]
        static List<int> lookupSheetIds(string spreadsheetId, List<string> sheetTitles)
        {
            Dictionary<string, object> lookup = GetSheetsInGoogleSheet(spreadsheetId);
            List<int> sheetIdsInt = new List<int>();

            if (lookup.ContainsKey("sheetTitles"))
            {
                var values = new object();
                lookup.TryGetValue("sheetTitles", out values);

                List<string> _sheetTitles = (List<string>)values;
                int sheetIndex = -1;
                foreach (string title in sheetTitles)
                {
                    for (int i = 0; i < _sheetTitles.Count; i++)
                    {
                        if (_sheetTitles[i] == title)
                        {
                            sheetIndex = i;
                        }
                    }
                    values = new object();
                    lookup.TryGetValue("sheetIds", out values);
                    List<string> sheetIds = (List<string>)values;

                    int sheetId = Int32.Parse(sheetIds[sheetIndex]);

                    sheetIdsInt.Add(sheetId);
                }
            }
            else
            {
                // No sheet ids were found
                sheetIdsInt = null;
            }
            return sheetIdsInt;
        }

        [IsVisibleInDynamoLibrary(false)]
        static bool IsNumeric(object Expression)
        {
            double retNum;

            bool isNum = Double.TryParse(Convert.ToString(Expression), System.Globalization.NumberStyles.Any, System.Globalization.NumberFormatInfo.InvariantInfo, out retNum);
            return isNum;
        }
    }
}
