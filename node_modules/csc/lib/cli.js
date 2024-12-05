#!/usr/bin/env node

'use strict'

const debug = require('debug')('csc')
const importLocal = require('import-local')

if (importLocal(__filename)) {
  debug('Using local install of CSC')
} else {
  require('.')()
}
