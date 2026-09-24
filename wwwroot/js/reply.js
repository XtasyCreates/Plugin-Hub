// The answer to a customer who sent a device id: the code and how to use it.
// German or English - the plug-ins themselves speak English.

export function replyText({ lang, customerName, productName, command, machineId, code, signature }) {
  const name = customerName?.trim() ?? '';

  if (lang === 'en') {
    return [
      `Hi${name ? ` ${name}` : ''},`,
      '',
      `thank you for buying ${productName}! Here is your activation code:`,
      '',
      code,
      '',
      'To activate it:',
      `1. Start Rhino and run the command ${command}.`,
      '2. The activation window opens. Paste the code under "2)" and click "Activate".',
      '',
      `The code belongs to the device ID you sent me (${machineId}). If you move to a new computer or reinstall Windows, just send me the new device ID and you will get a new code.`,
      '',
      'Best regards,',
      signature,
    ].join('\n');
  }

  return [
    `Hallo${name ? ` ${name}` : ''},`,
    '',
    `danke für deinen Kauf von ${productName}! Hier ist dein Aktivierungscode:`,
    '',
    code,
    '',
    'So aktivierst du das Plugin:',
    `1. Starte Rhino und gib den Befehl ${command} ein.`,
    '2. Das Aktivierungsfenster öffnet sich. Füge den Code unter „2)“ ein und klicke auf „Activate“.',
    '',
    `Der Code gehört zu der Geräte-ID, die du mir geschickt hast (${machineId}). Bekommst du einen neuen Rechner oder installierst Windows neu, schick mir einfach die neue Geräte-ID – dann bekommst du einen neuen Code.`,
    '',
    'Viele Grüße',
    signature,
  ].join('\n');
}
