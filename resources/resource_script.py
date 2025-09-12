#!/usr/bin/env python3
"""

"""

import os
import sys
import time
import shutil
import tarfile
import schedule
import logging
import requests
from datetime import datetime, timedelta
from pathlib import Path
from mcrcon import MCRcon

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('/app/backup.log')
    ]
)

logger = logging.getLogger(__name__)