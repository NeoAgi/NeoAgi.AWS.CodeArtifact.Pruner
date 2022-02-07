# NeoAgi.AWS.CodeArtifact.Pruner

## Usage

Typical usage should be done from pulling a docker image  and running it:

```
$ docker pull public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest
$ docker run public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain <domain> --repository <repository,repository> [OPTIONS]
```

AWS credentials must be set prior to running.  This can be done by exposing them through docker using the `-e` (assuming $KEY_ID and $KEY_SECRET have been set):

```
docker run -e AWS_ACCESS_KEY_ID=${KEY_ID} -e AWS_SECRET_ACCESS_KEY=${KEY_SECRET} -t public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain neoagi --repository neoagi
```

or with an `--env-file` directive:

```
docker run --env-file ~/aws-creds.env -t public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain neoagi --repository neoagi
```

For more information see [docker run ENV Reference](https://docs.docker.com/engine/reference/run/#env-environment-variables).

## Options

A full list of options may be generated using the `--help` flag.  Currently they generate:

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

These options may be specified at the end of the `docker run` command.  For more information see [docker run CMD Reference](https://docs.docker.com/engine/reference/run/#cmd-default-command-or-options).

### Dry Run

It may be useful to execte the pruner without modifying the repository. This may be performed by supplying `--dry-run true` to the list of parameters.  This will ommit the following line in the output as a Warning:

`{ "time": "2022-02-07 09:08:23.3870", "level": "WARN", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "Dry Run is enabled.  No changes will be made." }`

And no writes will be made to the repository.  No further modificiations will be made to any logs.  

### Caching

The `--cacheLocation` directive allows for persisting a package cache of the AWS API Results preventing excessive calls to API Quotas.  When set the TTL may optionally be set using `--cacheTtl` to indicate the number of hours the cache should be considered valid.  Any writes to the AWS APIs will immediately invalidate the cache.

### Performance

Pruner operations are limited by remote calls to the AWS APIs.  Communication to the AWS APIs will allow up to `((Number of Processors exposes to the Container - 1) * 2)` concurrent activities unless specified by the `--threads` setting.  Setting this to 0 will force serial operations and setting this value too high will cause CPU to be consumed managing concurrency than pefroming work.  The default is suggested or some multiple of the availabile processors.

### Logging

`--logLevel` allows more information to be emitted to the output.  Information is used by default which may benefit from adding `--checkpointInterval` to provide an activity indicator if desired.  For reference:

* `--logLevel warning` provides minimal information to the output on process activity limiting logs to errors or critical events that prevent execution.
* `--logLevel information` will emit key progress points of the process of the pruner containing package counts and modifications attempted.
* `--logLevel debug` will emit considerably more information about the process of the pruner and package details.
* `--logLevel trace` includes debug information and internal coordination material such as thread blocking and context switching.  

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