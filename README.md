# NeoAgi.AWS.CodeArtifact.Pruner

## Usage

Typical usage should be done from pulling a docker image  and running it:

```
$ docker pull public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest
$ docker run public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain <domain> --repository <repository,repository> [OPTIONS]
```

AWS credentials must be set prior to running.  This can be done by exposing them through docker using the `e` or `--env-file` option:

```
docker run -e AWS_ACCESS_KEY_ID=${KEY_ID} -e AWS_SECRET_ACCESS_KEY=${KEY_SECRET} -t public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain neoagi --repository neoagi
```

```
$ docker run public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --help
USAGE: NeoAgi.AWS.CodeArtifact.Pruner v1.0.0.0

Options:
-c, --cacheLocation             | Cache Location - Directory to hold for local cache. (optional)
-ll, --loglevel                 | Logging Level - Minimum Logging Level to emit.  Availabile options are None, Trace, Debug, Information, Warning, Error, Critical.  Default is Information. (optional)
-ttl, --cacheTtl                | Cache TTL - Number of hours to consider the Cache Location valid for.  Default is 8 hours.  Set to 0 to delete cache. (optional)
-a, --account                   | AWS Account ID - AWS Account ID to use.  Only necessary if domain and namespace are not unique to the principal provided. (optional)
-d, --domain                    | Artifact Domain - AWS Artifact Domain to query with.
-r, --repository                | Artifact Repository - AWS Artifact Repository to query with.
-t, --threads                   | Concurrency Maximum Limit - Maximum threads that will be used for background tasks.  Set to 0 to assume Processor Count - 1. (optional)
-p, --pageLimit                 | Page Limit - Maximum number of pages to return from AWS Calls.  Default to 50 pages, or about 2,500 packages. (optional)
-i, --checkpointInterval        | Checkpoint Interval - Number of items processed before a checkpoint information log is emitted.  Set to 0 to disable. (optional)
-dr, --dry-run                  | Dry Run - If set to true, no changes will be commited but logs will indiate what would have occurred.  Default is false. (optional)
```

## Errors

### No AWS Credentials Provided
The following error will be raised if AWS Credentials are not provided to the container.

```
$ docker run public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest
{ "time": "2022-02-07 16:15:07.4283", "level": "INFO", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "Starting discovery for \/", "repository": "", "domain": "" }
{ "time": "2022-02-07 16:15:07.4607", "level": "DEBUG", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "API Cache was missing or stale.  Skipping." }
Unhandled exception. System.AggregateException: One or more errors occurred. (Unable to get IAM security credentials from EC2 Instance Metadata Service.)
```

Review the Usage Section to set appropriate ENV Variables and try again.