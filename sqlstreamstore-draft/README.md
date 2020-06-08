# Propulsion SqlStreamStore Projector (without Kafka emission)

This project was generated using:

    dotnet new -i Equinox.Templates # just once, to install/update in the local templates store
    # add -k to add Kafka Projection logic
    dotnet new proProjector -s sqlStreamStore # use --help to see options

## Usage instructions

0. establish connection strings etc. per https://github.com/jet/equinox README

        $env:SQLSTREAMSTORE_CONNECTION="AccountEndpoint=https://....;AccountKey=....=;" # or use -c
        $env:SQLSTREAMSTORE_CHECKPOINTS_CONNECTION="equinox-test" # or use --checkpoints

// TODO: propulsion tool needs to be updated for that.
1. Use the `eqx` tool to initialize the checkpoints container

        dotnet tool install -g Equinox.Tool # only needed once

        # (either add environment variables as per step 0 or use -c/--checkpoints to specify them)

        # create a schema for the checkpoints db
        eqx init sqlStreamStoreCheckpoints
         
2. To run an instance of the Projector:

        # (either add environment variables as per step 0 or use -s/-d/-c to specify them)

        # `-g default` defines the Projector Group identity - each id has a separate checkpoint in the EQUINOX_COSMOS_CONTAINER
        # sqlStreamSource specifies the source details
       dotnet run -- -g default sqlStreamSource
