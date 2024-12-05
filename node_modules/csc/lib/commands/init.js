'use strict'

const _ = require('lodash')
const { spawn } = require('child-process-promise')
const inquirer = require('inquirer')

const desc = 'Run config initialization wizard'

function getPeerDependencies(pkg) {
  switch (pkg) {
    case 'eslint-config-concise':
      return [
        'eslint',
        'eslint-plugin-eslint-comments',
        'eslint-plugin-html',
        'eslint-plugin-markdown',
        'eslint-plugin-node',
        'eslint-plugin-promise',
      ]
    case 'eslint-config-concise-esnext':
      return getPeerDependencies('eslint-config-concise').concat([
        'eslint-plugin-babel',
      ])
    case 'eslint-config-concise-react':
      return ['eslint', 'eslint-plugin-react']
    case 'eslint-config-control-freak':
      return getPeerDependencies('eslint-config-concise').concat([
        'eslint-plugin-filenames',
        'eslint-plugin-import',
      ])
    default:
  }
  return []
}

async function handler() {
  const ui = new inquirer.ui.BottomBar()
  const answers = await inquirer.prompt([
    {
      type: 'checkbox',
      name: 'packages',
      message: 'Which packages do you want to install?',
      default: ['eslint-config-concise'],
      choices: [
        'csc',
        'eslint-config-concise',
        'eslint-config-concise-react',
        'eslint-config-concise-style',
        'eslint-config-control-freak',
      ],
    },
  ])

  const installPackages = _.uniq(
    _.flatMap(answers.packages, (pkg) => getPeerDependencies(pkg)).concat(
      answers.packages,
    ),
  )
  const npmArgs = installPackages.reduce((acc, pkg) => [...acc, pkg], [
    'install',
    '--color',
    'always',
    '-D',
  ])
  await spawn('npm', npmArgs, { stdio: ['inherit', ui.log, 'inherit'] })
}

module.exports = {
  desc,
  handler,
}
