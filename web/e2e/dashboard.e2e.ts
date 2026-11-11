import { Page, expect, test } from '@playwright/test';

const SHOTS = 'e2e/screenshots';
/** The default seed raises its first alarm within a few ticks; this is a generous cap. */
const MAX_TICKS = 200;

async function activeAlarmCount(page: Page): Promise<number> {
  return page.getByTestId('active-alarm').count();
}

test('raise an alarm, acknowledge it, and see the dashboard update', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Meterbench', level: 1 })).toBeVisible();

  // Start from the seed so the run does not depend on leftover state.
  await page.getByRole('button', { name: 'Reset seed' }).click();
  await expect(page.getByRole('button', { name: 'Reset seed' })).toBeEnabled();
  await expect(page.getByRole('heading', { name: 'Active alarms (0)' })).toBeVisible();
  const devices = page.getByRole('navigation', { name: 'Devices' }).getByRole('button');
  await expect(devices.first()).toBeVisible();
  // The selected device's subtitle must stay readable on the blue background (I03c).
  const selected = page.locator(".device[aria-pressed='true']");
  await expect(selected).toHaveCount(1);
  await expect(selected.locator('.muted')).toHaveCSS('color', 'rgb(219, 231, 243)');

  const advance = page.getByRole('button', { name: 'Advance 1 tick' });
  await advance.click();
  await expect(advance).toBeEnabled();
  await expect(page.getByTestId('freshness')).toContainText('Tick 1');
  await page.screenshot({ path: `${SHOTS}/01-initial.png`, fullPage: true });

  let ticks = 1;
  while ((await activeAlarmCount(page)) === 0) {
    expect(ticks, `no alarm raised within ${MAX_TICKS} ticks`).toBeLessThan(MAX_TICKS);
    await advance.click();
    await expect(advance).toBeEnabled();
    ticks++;
  }

  // Pin the alarm by its text: acknowledging may re-sort the list if several are active.
  const label = (
    await page.getByTestId('active-alarm').first().locator('span').first().innerText()
  ).trim();
  const alarm = page.getByTestId('active-alarm').filter({ hasText: label });
  const ack = alarm.getByRole('button', { name: 'Acknowledge' });
  await expect(ack).toBeVisible();
  await expect(alarm.getByText('Acknowledged')).toHaveCount(0);
  await page.screenshot({ path: `${SHOTS}/02-alarm-raised.png`, fullPage: true });

  await ack.click();
  await expect(alarm.getByText('Acknowledged', { exact: true })).toBeVisible();
  await expect(alarm.getByRole('button', { name: 'Acknowledge' })).toHaveCount(0);
  await expect(alarm).toHaveClass(/acked/);
  await expect(
    page
      .getByRole('table')
      .filter({ hasText: 'State' })
      .getByText(/active, acknowledged/),
  ).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/03-alarm-acknowledged.png`, fullPage: true });
});
