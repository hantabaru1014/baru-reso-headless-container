#!/bin/bash

if [ ! -e "./DepotDownloader" ]; then
  echo "DepotDownloader not found"
  exit 1
fi

./DepotDownloader -app 2519830 -beta headless -betapassword $HEADLESS_PASSWORD -username $STEAM_USERNAME -password $STEAM_PASSWORD -dir ./Resonite -os linux -filelist ./depot-dl-list.txt
