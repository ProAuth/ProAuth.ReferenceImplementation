import path from 'node:path';

const images = [
  ['api', 'src/services/Procurement.Api/Procurement.Api.csproj', 'procurement-api', 'Workplace'],
  ['api', 'src/services/Fulfilment.Api/Fulfilment.Api.csproj', 'fulfilment-api', 'Workplace'],
  ['api', 'src/services/Treasury.Api/Treasury.Api.csproj', 'treasury-api', 'Workplace'],
  ['bff', 'src/apps/Workplace.Bff/Workplace.Bff.csproj', 'workplace-bff', 'Workplace'],
  [
    'bff',
    'src/apps/SupplierPortal.Bff/SupplierPortal.Bff.csproj',
    'supplier-bff',
    'SupplierPortal',
  ],
  ['bff', 'src/apps/FinanceDesk.Bff/FinanceDesk.Bff.csproj', 'finance-bff', 'FinanceDesk'],
  [
    'api',
    'src/clients/Integration.Worker/Integration.Worker.csproj',
    'integration-worker',
    'Workplace',
  ],
  ['api', 'labs/protocols/ProtocolLab.Api/ProtocolLab.Api.csproj', 'protocol-api', 'Workplace'],
  ['api', 'labs/protocols/Demo.Probe/Demo.Probe.csproj', 'conference-probe', 'Workplace'],
  [
    'api',
    'labs/protocols/ProtocolLab.Probe/ProtocolLab.Probe.csproj',
    'protocol-probe',
    'Workplace',
  ],
  [
    'api',
    'src/clients/Warehouse.Terminal/Warehouse.Terminal.csproj',
    'warehouse-terminal',
    'Workplace',
  ],
  ['bff', 'src/demo-console/DemoConsole/DemoConsole.csproj', 'demo-console', 'DemoConsole'],
  ['api', 'labs/unsafe/Demo.Browser/Demo.Browser.csproj', 'conference-browser', 'Workplace'],
  ['api', 'labs/unsafe/Demo.Attacker/Demo.Attacker.csproj', 'conference-attacker', 'Workplace'],
  ...['Api', 'Capture', 'Runner', 'Victim'].map((n) => [
    'api',
    `labs/unsafe/SecurityLab.${n}/SecurityLab.${n}.csproj`,
    `lab-${n.toLowerCase()}`,
    'Workplace',
  ]),
];
export function selectImages(service, includeLabs) {
  if (service && !images.some(([, , image]) => image === service))
    throw Error('Unknown service image.');
  return images.filter(([, project, image]) =>
    service ? image === service : includeLabs || project.startsWith('src/'),
  );
}

export function buildImages({ root, env, run, service, includeLabs }) {
  for (const [target, project, image, app] of selectImages(service, includeLabs))
    run(
      'docker',
      [
        'build',
        '-f',
        'deploy/compose/Dockerfile',
        '--target',
        target,
        '--build-arg',
        `PROJECT=${project}`,
        '--build-arg',
        `APP=${app}`,
        '--secret',
        `id=proauthfeed,src=${path.join(root, '.local/nuget.customer.config')}`,
        '--secret',
        `id=trustedca,src=${env.TRUSTED_CA_FILE}`,
        '-t',
        `mervikon/${image}:local`,
        '.',
      ],
      env,
    );
}
