'/*******************************************************************************
'* Copyright 2012-2013 True Systems or its affiliates. All Rights Reserved.
'* 
'* Licensed under the Apache License, Version 2.0 (the "License"). You may
'* not use this file except in compliance with the License. A copy of the
'* License is located at
'* 
'* http://www.apache.org/licenses/LICENSE-2.0.html
'* 
'* or in the "license" file accompanying this file. This file is
'* distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
'* KIND, either express or implied. See the License for the specific
'* language governing permissions and limitations under the License.
'*******************************************************************************/
' /
' 
'   Version history :
'                    1.0 - Initial version written by Sergio Oehler on Jan 2013
'                    Currently does not support recursive AWS Virtual Folders copy
'                                                                                                          / 
'/********************************************************************************************************** 
'* Application configuration settings defibed on file s3doppelcli.exe.config                
'*
'* AWSAccessKey       - Security credential provided by Amazon AWS Subscription use 
'*                      your own and NEVER distribute to other people
'* AWSSecretKey       - Security credential provided by Amazon AWS Subscription use 
'*                      your own and NEVER distribute to other people
'* AWSRegionEndpoint  - AWS region where your S3 bucket lives currently available 
'*                      are eu-west-1, sa-east-1, us-east-1, ap-northeast-1, 
'*                      ap-southeast-1, ap-southeast-2, us-west-1, us-west-2   
'* 
'***********************************************************************************************************/   

Imports System
Imports System.Collections.Specialized
Imports System.Configuration
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Threading
Imports System.Collections
Imports Amazon
Imports Amazon.S3
Imports Amazon.S3.Model

Enum CommandType
    CopyFromS3C = 1
    CopyToS3C = 2
End Enum


Module S3C

    Private S3DoppelEventLog As EventLog
    Private appConfig As NameValueCollection
    Private commands As Hashtable

    Sub Main()

        ParseCommandLine()
        CreateEventLog()
        LogInfo("S3C copy started at " & Now.ToString, EventLogEntryType.Information)
        If commands("COMMAND") = "1" Then
            CopyToS3Bucket(commands("BUCKET"), commands("BUCKETPATH"), commands("LOCALPATH"))
        Else
            CopyFromS3Bucket(commands("BUCKET"), commands("BUCKETPATH"), commands("LOCALPATH"))
        End If
        LogInfo("S3C copy finished", EventLogEntryType.Information)
        S3DoppelEventLog.Dispose()
        Environment.Exit(0)

    End Sub

    Public Sub CreateEventLog()

        S3DoppelEventLog = New EventLog

        If Not System.Diagnostics.EventLog.SourceExists("S3C") Then
            System.Diagnostics.EventLog.CreateEventSource("S3C", _
            "")
        End If
        S3DoppelEventLog.Source = "S3C"
        S3DoppelEventLog.Log = ""

        'read application configuration
        appConfig = ConfigurationManager.AppSettings
        DebugInfo("S3C application event log source created")

    End Sub

    Private Sub ParseCommandLine()

        If Environment.CommandLine.ToUpper.IndexOf("-HELP") > 0 Then
            Console.WriteLine("S3C - (c)True Systems")
            Console.WriteLine()
            Console.WriteLine("Utility to copy entire folder contents from/to S3 ")
            Console.WriteLine("(copy only newer files from S3 if file already exist locally")
            Console.WriteLine()
            Console.WriteLine("Usage:")
            Console.WriteLine("        --COMMAND=1-COPY TO, 2-COPY FROM           [REQUIRED]")
            Console.WriteLine("        --BUCKET=S3_bucket_name                    [REQUIRED]")
            Console.WriteLine("        --BUCKETPATH=S3_bucket_folder path, if not [OPTIONAL]")
            Console.WriteLine("          informed S3C will copy all bucket")
            Console.WriteLine("          content recursively")
            Console.WriteLine("        --LOCALPATH=local_machine_path             [REQUIRED]")
            Console.WriteLine("          if it does not exist it will be created    ")
            Console.WriteLine("        --SILENT=Y , if exists suppress            [OPTIONAL]")
            Console.WriteLine("          console mesSages                        ")
            Console.WriteLine("        --DEBUG=Y , if exists outputs              [OPTIONAL]")
            Console.WriteLine("          extra execution information                ")
            Console.WriteLine("        --EVENTLOG=Y , if exists outputs           [OPTIONAL]")
            Console.WriteLine("          messages to windows event log               ")
            Console.WriteLine("")
            Console.WriteLine("Note: AWS S3 resource names are case sensitive")
            Console.WriteLine("")
            Console.WriteLine("Output : exitcode (ERRORLEVEL) 0 if execution was OK or 1 in case of error")
            Environment.Exit(0)
        End If

        commands = New Hashtable
        Dim sep() As String = {"--"}
        Dim parameters() As String = Environment.CommandLine.Split(sep, StringSplitOptions.None)
        Dim parameter() As String

        For k As Integer = 0 To parameters.Length - 1
            parameter = parameters(k).Split("=")
            If parameter.Length > 1 Then
                commands.Add(parameter(0).ToUpper.Trim, parameter(1).Trim)
            End If
        Next

        If CheckCommand("COMMAND") = False Then Environment.Exit(1)
        If CheckCommand("BUCKET") = False Then Environment.Exit(1)
        If CheckCommand("BUCKETPATH") = False Then
            commands.Add("BUCKETPATH", "")
        End If
        If CheckCommand("LOCALPATH") = False Then Environment.Exit(1)

    End Sub

    Private Function CheckCommand(ByVal C As String) As Boolean

        Try
            If commands(C) = "" Then
                Console.WriteLine("ERROR, empty " & C & " parameter")
                Return False
            End If

        Catch ex As NullReferenceException
            Console.WriteLine("ERROR, missing " & C & " parameter")
            Return False
        End Try

        Return True

    End Function

    Private Sub CopyFromS3Bucket(ByVal Bucket As String, ByVal KeyPrefix As String, ByVal LocalFolder As String)

        Dim s3Client As AmazonS3                'S3 Client Object
        Dim S3KeysLastModified As Hashtable     'list of s3 bucket content, Keys(file names) and Last Modification Dates 
        Dim listRequest As ListObjectsRequest   'collection of s3 Bucket entries
        Dim objRequest As GetObjectRequest      'S3 object content requestes from AWS
        Dim DicEntry As DictionaryEntry         'dictionaryEntry object to parse S3KeysLastModified hash table
        Dim d As Date                           'temporary variable for data conversion
        Dim timestart As Date = Now



        'Create Amazon AWS S3 Client pointing to configured region
        Try
            s3Client = AWSClientFactory.CreateAmazonS3Client(RegionEndpoint.GetBySystemName(appConfig("AWSRegionEndpoint")))
            LogInfo("Connected to S3 region endpoint " & appConfig("AWSRegionEndpoint"), EventLogEntryType.Information)
        Catch ex As AmazonS3Exception
            If (ex.ErrorCode <> Nothing And (ex.ErrorCode.Equals("InvalidAccessKeyId") Or ex.ErrorCode.Equals("InvalidSecurity"))) Then
                LogInfo("Error connecting to AWS S3 check provided AWS credentials", EventLogEntryType.Error)
            End If
            LogInfo("Error connecting to AWS S3 : " & ex.Message, EventLogEntryType.Error)
            Environment.Exit(1)
        End Try

        Try

            S3KeysLastModified = New Hashtable
            listRequest = New ListObjectsRequest
            listRequest.BucketName = Bucket             'As it sys AWS Bucket Name
            listRequest.WithPrefix(KeyPrefix)           'a.k.a Folder


            'list matching files (reading into memory may be a problem if 1000s of file entries exist)
            Using listResponse As ListObjectsResponse = s3Client.ListObjects(listRequest)
                For Each entry As S3Object In listResponse.S3Objects
                    If entry.Size > 0 Then
                        S3KeysLastModified.Add(entry.Key, entry.LastModified)
                        DebugInfo("AWS S3 Key retrivied: " & entry.Key.ToString & "   Size: " & entry.Size.ToString & "   Date: " & entry.LastModified & " from S3 Bucket " & Bucket)
                        If DateTime.TryParse(entry.LastModified.Replace("GMT", ""), d) = False Then
                            DebugInfo("Failed to parse data [" + entry.LastModified + "] from S3 Object : " & entry.Key & " from S3 Bucket " & Bucket)
                        End If
                    End If
                Next
            End Using


            'copy previosuly listed files from S3 to local folder
            For Each DicEntry In S3KeysLastModified
                objRequest = New GetObjectRequest().WithBucketName(Bucket).WithKey(DicEntry.Key)
                Using objResponse As GetObjectResponse = s3Client.GetObject(objRequest)
                    Dim dest As String = Path.Combine(LocalFolder, DicEntry.Key).Replace("/", "\").Replace("\" & appConfig("S3ObjectKeyPrefix") & "\", "\")
                    If File.Exists(dest) Then
                        'file does exist locally replace it if it is older(ignoring GMT for the moment, needs improvement)
                        DebugInfo("File " + dest + " exixts locally")
                        If DateTime.TryParse(DicEntry.Value.Replace("GMT", ""), d) = True Then
                            'data conersion has succeded, replace local file only if older
                            If Date.Compare(d, File.GetLastWriteTime(dest)) <> 0 Then
                                DebugInfo("Replacing existing local" + dest + "file and stamping S3 object original last update date/time " & d.ToString)
                                File.Delete(dest)
                                objResponse.WriteResponseStreamToFile(dest)
                                File.SetLastWriteTime(dest, d)
                            Else
                                DebugInfo("Local file " + dest + " is uptodate, no need to copy from S3 " & d.ToString & " from S3 Bucket " & Bucket)
                            End If
                        Else
                            'data parsing from AWS failed copy anyway but do not stamp date
                            DebugInfo("Data parsing from S3 object failed to file " & dest & " replacing file anyway but not stamping S3 object original Date/Tipe " & d.ToString)
                            File.Delete(dest)
                            objResponse.WriteResponseStreamToFile(dest)
                        End If
                    Else
                        'file does not exists locally copy anyway
                        objResponse.WriteResponseStreamToFile(dest)
                        DebugInfo("Creating local file " & dest & " file file previously did not exist ")
                        If Date.Compare(d, File.GetLastWriteTime(dest)) <> 0 Then
                            DebugInfo("Stamping newly created " & dest & " file with S3 object original last update date/time " & d.ToString)
                            File.SetLastWriteTime(dest, d)
                        Else
                            DebugInfo("Data parsing from S3 object failed to newly created file " & dest & " ,proceeding without stamping S3 object original Date/Tipe " & d.ToString)
                        End If
                    End If
                End Using
            Next DicEntry

        Catch ex As AmazonS3Exception

            If (ex.ErrorCode <> Nothing And (ex.ErrorCode.Equals("InvalidAccessKeyId") Or ex.ErrorCode.Equals("InvalidSecurity"))) Then
                LogInfo("Error connecting to AWS S3 check provided AWS credentials", EventLogEntryType.Error)
            Else
                LogInfo("An error occurred reading from AWS S3, with the message " & ex.Message & " when reading an object ", EventLogEntryType.Error)
            End If
            Environment.Exit(1)
        Catch ex2 As IOException
            LogInfo("An error occurred writing to the local file system " & ex2.Message, EventLogEntryType.Error)
            Environment.Exit(1)
        Finally
            S3KeysLastModified = Nothing
            listRequest = Nothing
            objRequest = Nothing
            DicEntry = Nothing
            d = Nothing
            s3Client = Nothing
        End Try

        If Environment.CommandLine.ToUpper.IndexOf("VERBOSE") Then
            Console.WriteLine("Elapsed Time : " & Now.Subtract(timestart).TotalSeconds.ToString & " second(s)")
        End If

    End Sub

    Private Sub CopyToS3Bucket(ByVal Bucket As String, ByVal KeyPrefix As String, ByVal LocalFile As String)

        Dim s3Client As AmazonS3
        Dim timestart As Date = Now
        Dim objRequest As PutObjectRequest
        Dim responseRequest As PutObjectResponse
        Dim fileStream As FileStream

        'Create Amazon AWS S3 Client pointing to configured region
        Try
            s3Client = AWSClientFactory.CreateAmazonS3Client(RegionEndpoint.GetBySystemName(appConfig("AWSRegionEndpoint")))
            LogInfo("Connected to S3 region endpoint " & appConfig("AWSRegionEndpoint"), EventLogEntryType.Information)
        Catch ex As AmazonS3Exception
            If (ex.ErrorCode <> Nothing And (ex.ErrorCode.Equals("InvalidAccessKeyId") Or ex.ErrorCode.Equals("InvalidSecurity"))) Then
                LogInfo("Error connecting to AWS S3 check provided AWS credentials", EventLogEntryType.Error)
            End If
            LogInfo("Error connecting to AWS S3 : " & ex.Message, EventLogEntryType.Error)
            Environment.Exit(1)
        End Try

        'Create a PutObject request
        objRequest = New PutObjectRequest
        objRequest.BucketName = Bucket
        objRequest.Key = KeyPrefix

        'Open file
        Try

            fileStream = New FileStream(LocalFile, FileMode.Open)

            'Set filestream
            objRequest.InputStream = fileStream

        Catch ex As Exception
            LogInfo("Local file not found.", EventLogEntryType.Error)
            Environment.Exit(1)
        End Try

        Try

            'Put object to s3 bucket
            responseRequest = s3Client.PutObject(objRequest)

        Catch ex As Exception
            LogInfo("Operation error: " & responseRequest.ToString, EventLogEntryType.Error)
            Environment.Exit(1)
        End Try

    End Sub

    Private Sub DebugInfo(ByVal s As String)

        Try
            If commands("DEBUG") = "Y" Then
                LogInfo("DEBUG--> " & s, EventLogEntryType.Information)
            End If
        Catch ex As Exception
            'COMMANDLINE DEBUG OPTION NOT SPECIFIED
        End Try

    End Sub

    Private Sub LogInfo(ByVal s As String, ByVal t As EventLogEntryType)

        Try
            If commands("EVENTLOG") = "Y" Then
                S3DoppelEventLog.WriteEntry(s, t)
            End If
        Catch ex As Exception
            'COMMANDLINE EVENTLOG OPTION NOT SPECIFIED
        End Try


        Try
            If commands("SILENT") = "Y" Then
                'DO NOT OUTPUT MESSAGES
            Else
                Console.WriteLine(s)
            End If
        Catch ex As NullReferenceException
            'SILENT WAS NOT SPECIFIED SO WRITE THOSE MESSAGES OUT
            Console.WriteLine(s)
        End Try

    End Sub

End Module
