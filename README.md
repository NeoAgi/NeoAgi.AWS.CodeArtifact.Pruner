# NeoAgi.AWS.CodeArtifact.Pruner

AWS CodeArtifact will often be used in two methods:

1. As a pull through cache to an upstream repository
1. As an artifact store for non-public packages

The Pruner exposes a policy based way of managing the assets stored within CodeArtifact Repositories to limit the storage needed for the service, while retaining key assets for the lifespan desired.  

## Usage

Typical usage should be done from pulling a docker image  and running it:

```
$ docker pull public.ecr.aws/neoagi/neoagi.aws.codeartifact.pruner:latest
$ docker run public.ecr.aws/neoagi/neoagi.aws.codeartifact.pruner:latest --domain <domain> --repository <repository,repository> [OPTIONS]
```

AWS credentials must be set prior to running.  This can be done by exposing them through docker using the `-e` (assuming $KEY_ID and $KEY_SECRET have been set):

```
docker run -e AWS_ACCESS_KEY_ID=${KEY_ID} -e AWS_SECRET_ACCESS_KEY=${KEY_SECRET} -t public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --domain neoagi --repository neoagi --policy <policy JSON>
```

or with an `--env-file` directive:

```
docker run --env-file ~/aws-creds.env -t public.ecr.aws/neoagi/neoagi.aws.codeartifact.pruner:latest --domain neoagi --repository neoagi --policy <policy JSON>
```

For more information see [docker run ENV Reference](https://docs.docker.com/engine/reference/run/#env-environment-variables).

## Options

A full list of options may be generated using the `--help` flag.  Currently they generate:

```
$ docker run public.ecr.aws/x7q2k3a7/neoagi.aws.codeartifact.pruner:latest --help
USAGE: NeoAgi.AWS.CodeArtifact.Pruner v1.0.1.0
NeoAgi, LLC - 2021 NeoAgi, LLC

Options:
-ll, --loglevel                 | Logging Level - Minimum Logging Level to emit.  Availabile options are None, Trace, Debug, Information, Warning, Error, Critical.  Default is Information. (optional)
-a, --account                   | AWS Account ID - AWS Account ID to use.  Only necessary if domain and namespace are not unique to the principal provided. (optional)
-d, --domain                    | Artifact Domain - AWS Artifact Domain to query with.
-r, --repository                | Artifact Repository - AWS Artifact Repository to query with.
-p, --pageLimit                 | Page Limit - Maximum number of pages to return from AWS Calls.  Default to 50 pages, or about 2,500 packages. (optional)
-i, --checkpointInterval        | Checkpoint Interval - Number of items processed before a checkpoint information log is emitted.  Set to 0 to disable. (optional)
-dr, --dry-run                  | Dry Run - If set to true, all logs will report as if changed occurred yet no modifications will be made.  Default is false. (optional)
--parallelism                   | Parallalism - The number of concurrent tasks to process at once.  A higher number will increase TPS. (optional)
--tps                           | Maximum Transactions Per Second - A hard limit of transactions per second to enforce. (optional)
--policy                        | Policy JSON - Policy as a JSON String. Structure: [{'Namespace': 'PREFIX.*','VersionsToKeep':INT}]
```

These options may be specified at the end of the `docker run` command.  For more information see [docker run CMD Reference](https://docs.docker.com/engine/reference/run/#cmd-default-command-or-options).

### Dry Run

It may be useful to execte the pruner without modifying the repository. This may be performed by supplying `--dry-run` to the list of parameters.  This will ommit the following line in the output as a Warning:

`{ "time": "2022-02-07 09:08:23.3870", "level": "WARN", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "Dry Run is enabled.  No changes will be made." }`

And no writes will be made to the repository.  Logs will emit what DeletePackageVersion would have been made, all other log entries will be emitted without modification.  

### Performance (Transactions Per Section & Parallelism)

Pruner operations are limited by remote calls to the AWS APIs with `HttpThrottleException` being emitted when rate limiting is applied.  If rate limiting is experienced the Pruner will re-queue the failed operation and 
enter an expotnetial backoff algorithm followed by a ramp up period to prevent further rate limiting.  

Performance is capped by the `--tps` flag which provdes a maxiumum of transactions per second to be sent.  Smoothing out TPS can be configured by adjusting the `--parallelism` option which limits how many requests can 
be "in flgiht" at once.  Depending on latency of each API Call, a high degree of parallellism may fall under the TPS appllied or it may cause bursts of calls followed by blocking to remain under the TPS set.  An ideal ratio 
will strike a smooth compromize between waiting and processing while staying under the TPS.  Setting `--logLevel DEBUG` may assist to dial in smoothing as it emits log messages reporting action queue depth, observed TPS, and 
the mount of time waiting once TPS has been reached and any periods where backoff or rampup is occurring.  

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
$ docker run public.ecr.aws/neoagi/neoagi.aws.codeartifact.pruner:latest
{ "time": "2022-02-07 16:15:07.4283", "level": "INFO", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "Starting discovery for \/", "repository": "", "domain": "" }
{ "time": "2022-02-07 16:15:07.4607", "level": "DEBUG", "method": "NeoAgi.AWS.CodeArtifact.Pruner.Worker", "message": "API Cache was missing or stale.  Skipping." }
Unhandled exception. System.AggregateException: One or more errors occurred. (Unable to get IAM security credentials from EC2 Instance Metadata Service.)
```

Review the Usage Section to set appropriate ENV Variables and try again.

## Policy

The `--policy` directive allows the user to supply a Policy Defintiion for the runtime to evaluate.  Policies must be set using an array of [PersistVersionCount](https://github.com/NeoAgi/NeoAgi.AWS.CodeArtifact.Pruner/blob/main/NeoAgi.AWS.CodeArtifact.Pruner/Policies/PersistVersionCount.cs) 
which will be parsed into runtime evaluation criteria.  The following will keep the last 100 versions of any namespace starting with NeoAgi, and the last 3 versions of all others:

```json
[
  {
    "Namespace": "NeoAgi*",
    "VersionsToKeep": 100
  },
  {
    "Namespace": "*",
    "VersionsToKeep": 3
  }
]
```

The policy parser is primitive at this stage and will be improved upon over time.  Any JSON Errors will be omitted directly for the end user to work with.  

Tips:
- Remove all whitespace in the JSON (e.g. Do not pretty print).
- Field names MUST be double quoted.  Single quotes are not handled properly.  
- Properly escape the data for your shell (e.g. `--policy "[{\"Namespace\": \"*\",\"VersionsToKeep\":3}]"`)
- If no default policy (`Namespace: *`) is included, the policy manager will add a default rule keeping the last 100 versions

For example the following is the above pretty print honoring JSON Tips:
`--policy "[{\"Namespace\": \"NeoAgi*\",\"VersionsToKeep\":100},{\"Namespace\":\"*\",\"VersionsToKeep\":3}]"`
