#!/bin/bash

# ./DepotDownloader -app 2519830 -beta headless -betapassword $HEADLESS_PASSWORD -username $STEAM_USERNAME -password $STEAM_PASSWORD -dir ./Resonite -os linux -filelist ./depot-dl-list.txt

docker run -it -v $PWD/Resonite:/data steamcmd/steamcmd:latest +force_install_dir /data +login $STEAM_USERNAME $STEAM_PASSWORD "+app_update 2519830 -beta headless -betapassword $HEADLESS_PASSWORD" +quit
