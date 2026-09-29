/** How to add goods to the iPhone Home Screen (staff devices). Same steps as ezbook's, worded for the «Заказы» app. */
export function GoodsIosSteps() {
  return (
    <ol className="list-decimal pl-5 mt-2 flex flex-col gap-1.5 text-sm text-ink-soft" aria-label="Как добавить приложение на экран «Домой»">
      <li>Нажмите кнопку «Поделиться» внизу или вверху экрана. Если её не видно — сначала нажмите «⋯».</li>
      <li>Выберите «На экран «Домой»» (если пункта нет в списке — пролистайте вниз), затем «Добавить».</li>
      <li>
        Откройте приложение с новой иконки на экране «Домой» и <b className="font-medium text-ink">войдите заново</b> — приложение не видит вход, выполненный в браузере.
      </li>
      <li>Зайдите в «Устройства» и включите уведомления — айфон спросит разрешение.</li>
    </ol>
  )
}
