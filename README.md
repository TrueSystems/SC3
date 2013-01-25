S3C
===


Project Setup

 1) Create a local copy of app.config.template named App.config and add this file to SC3 project
                                             
 2) Update configuration parameters inside App.config according to your needs


How to use S3C
           
            Utility to copy recursively entire bucket/folder contents from S3 to local path
            (copy only newer files from S3 if file already exist locally)
            
            Usage:
                    --BUCKET=S3_bucket_name                      [REQUIRED]
                    --BUCKETPATH=S3_bucket_folder path           [OPTIONAL]
                      if not informed whole bucket wil be copied 
                    --LOCALPATH=local_machine_path               [REQUIRED]
                      if it does not exist it will be created    
                    --SILENT=Y , if exists suppress              [OPTIONAL]
                      console messages                            
                    --DEBUG=Y , if exists outputs                [OPTIONAL]
                      extra execution information                
                    --EVENTLOG=Y , if exists outputs             [OPTIONAL]
                      messages to windows event log               
            
            Note: AWS S3 resource names are case sensitive
            
            Output : exit code (ERROR-LEVEL) 0 if execution was OK or 1 in case of error
           
            Also you have to create an application config file (s3c.exe.config) to
            inform you AWS credentials and preferred S3 endpoint as follows (see project setup above):

           <?xml version="1.0" encoding="utf-8"?>
           <configuration>
             <appSettings>
                 <add key="AWSAccessKey" value="use your own" />
                 <add key="AWSSecretKey" value="use your own" />
                 <add key="AWSRegionEndpoint" value="sa-east-1" />    
             </appSettings>  
           </configuration>
