import { useState } from 'react';
import type { DemoState } from '../hooks/useDemo';
import { useRemote } from '../hooks/useRemote';
import type { ObservatoryApi } from '../lib/api';
import { dateTime, money } from '../lib/format';
import { redactText } from '../lib/redaction';
import { CatalogView } from './CatalogView';
import { ErrorBox, SectionHeading } from './Common';

export function DataView({ api, products, onAsk }: {
  api: ObservatoryApi; products: DemoState['products']; onAsk: (message: string) => void;
}) {
  const remote = useRemote(api.demoData);
  const data = remote.error ? null : remote.data;
  const [section, setSection] = useState<'products' | 'orders' | 'policies'>('products');
  const [search, setSearch] = useState('');
  const [onlyCustomer, setOnlyCustomer] = useState(false);
  const orders = (data?.orders ?? []).filter((order) =>
    (!onlyCustomer || order.customerId === data?.customerId) &&
    `${order.id} ${order.customerId} ${order.productId} ${order.productTitle} ${order.status} ${order.trackingCode}`
      .toLocaleLowerCase('it-IT').includes(search.toLocaleLowerCase('it-IT')),
  );

  return <div className="stack data-view">
    <section className="panel view-panel" aria-label="Esplora i dati della demo">
      <SectionHeading eyebrow="Le fonti del dominio, non risposte generate" title="Dati usati dagli agenti">
        <button type="button" className="button button-small" disabled={remote.loading || products.loading}
          onClick={() => { remote.reload(); products.reload(); }}>Aggiorna dati</button>
      </SectionHeading>
      <p>Catalogo pubblico, ordini sintetici e policy dei servizi Catalog, Orders e Returns. Questa pagina è di sola lettura: non avvia agenti, non crea ordini e non modifica le regole della chat.</p>
      <div className="notice notice-info"><p>Vista didattica dell’intero dataset sintetico, inclusi ordini di altri clienti. <strong>Vederli qui non autorizza il bot a leggerli.</strong> Gli agenti mantengono il cliente assegnato dal backend; le immagini restano solo nella UI.</p></div>
      {remote.loading && <p role="status">Caricamento ordini e policy dai servizi…</p>}
      {remote.error && <ErrorBox title="Ordini e policy non disponibili" message={remote.error} retry={remote.reload} />}
      {data && <p className="muted">{redactText(data.notice)}<br />Cliente della chat: <strong>{data.customerId}</strong> · Data applicativa: {dateTime(data.asOf)}</p>}
      <div className="button-row" role="group" aria-label="Seleziona dati da esplorare">
        <button type="button" className="button" aria-pressed={section === 'products'} onClick={() => setSection('products')}>Prodotti ({products.data?.length ?? '…'})</button>
        <button type="button" className="button" aria-pressed={section === 'orders'} onClick={() => setSection('orders')}>Ordini ({data?.orders.length ?? '…'})</button>
        <button type="button" className="button" aria-pressed={section === 'policies'} onClick={() => setSection('policies')}>Policy ({data?.policies.length ?? '…'})</button>
      </div>
    </section>
    {section === 'products' && <CatalogView products={products} onAsk={(product) =>
      onAsk(`Vorrei informazioni sul prodotto #${product.id}: ${product.title}.`)} />}
    {section === 'orders' && <section className="panel view-panel" aria-label="Ordini sintetici presenti a sistema">
      <SectionHeading eyebrow="Servizio Orders · nessuna modifica" title="Ordini presenti a sistema" />
      <p className="muted">Il prezzo pagato e la data di consegna sono quelli usati per valutare i resi. “Chiedi dell’ordine” prepara soltanto il messaggio: consenso e invio rimangono espliciti.</p>
      <label className="catalog-search">Cerca negli ordini<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Ordine, cliente, prodotto, stato o tracking…" /></label>
      <label className="checkbox-row"><input type="checkbox" checked={onlyCustomer} onChange={(event) => setOnlyCustomer(event.target.checked)} />Solo ordini del cliente della chat</label>
      {data && <>
        <p className="small-label">{orders.length} di {data.orders.length} ordini</p>
        <div className="table-scroll" tabIndex={0} aria-label="Elenco completo degli ordini sintetici">
          <table><caption>Ordini sintetici · importi pagati distinti dai prezzi di listino</caption>
            <thead><tr><th scope="col">Ordine / cliente</th><th scope="col">Prodotto</th><th scope="col">Listino</th><th scope="col">Pagato</th><th scope="col">Outlet</th><th scope="col">Consegna</th><th scope="col">Stato / tracking</th><th scope="col">Chat</th></tr></thead>
            <tbody>{orders.map((order) => <tr key={order.id}>
              <th scope="row">{order.id}<small>{order.customerId}</small><small>{order.customerId === data.customerId ? 'Cliente della chat' : 'Altro cliente: non accessibile al bot'}</small></th>
              <td>#{order.productId}<small>{redactText(order.productTitle)}</small></td>
              <td>{money(order.listPrice, order.currency)}</td><td>{money(order.amountPaid, order.currency)}</td>
              <td>{order.outlet ? 'Sì' : 'No'}</td><td>{order.deliveredAt}</td>
              <td>{redactText(order.status)}<small>{redactText(order.trackingCode)}</small></td>
              <td><button className="button button-small" type="button" aria-label={`Prepara una domanda sull'ordine ${order.id}`}
                onClick={() => onAsk(`Mostra l'ordine ${order.id}.`)}>Chiedi dell’ordine</button></td>
            </tr>)}</tbody>
          </table>
        </div>
        {orders.length === 0 && <p className="muted">Nessun ordine corrisponde ai filtri.</p>}
      </>}
    </section>}
    {section === 'policies' && <section className="panel view-panel" aria-label="Policy sintetiche del servizio Returns">
      <SectionHeading eyebrow="Servizio Returns · regole esplicite" title="Policy di reso" />
      <p className="muted">Testi effettivi restituiti dal servizio, con priorità e versione. Non sono condizioni commerciali reali.</p>
      {data && <div className="stack">{data.policies.map((policy) => <article className="policy-card" key={policy.id}>
        <h3>{redactText(policy.title)}</h3><p className="small-label">{policy.id} · Priorità {policy.priority} · Versione {redactText(policy.version)}</p>
        <p className="policy-text">{redactText(policy.text)}</p>
      </article>)}</div>}
      {data?.policies.length === 0 && <p className="muted">Nessuna policy presente.</p>}
    </section>}
  </div>;
}
