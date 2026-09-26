import { useMemo, useState } from 'react';
import type { Product } from '../contracts';
import type { DemoState } from '../hooks/useDemo';
import { ErrorBox, SectionHeading } from './Common';
import { ProductCard } from './ProductCards';

export function CatalogView({ products: remote, onAsk }: {
  products: DemoState['products']; onAsk: (product: Product) => void;
}) {
  const [search, setSearch] = useState('');
  const products = useMemo(() => (remote.data ?? []).filter((product) =>
    `${product.id} ${product.title} ${product.description} ${product.category} ${product.brand ?? ''} ${product.sku} ${product.tags.join(' ')}`.toLowerCase().includes(search.toLowerCase()),
  ), [remote.data, search]);

  return <section className="catalog-panel catalog-view panel" aria-label="Catalogo pubblico DummyJSON">
    <SectionHeading eyebrow="Immagini fuori dal contesto LLM" title="Catalogo pubblico"><span className="badge badge-neutral">DummyJSON</span></SectionHeading>
    <p className="muted">Prodotti letti dal backend. Prezzi e disponibilità sono dati di esempio, non un negozio reale.</p>
    <p className="catalog-chat-hint">Il bot è in basso a destra, anche nella pagina Traccia. “Chiedi del prodotto” apre la stessa chat e prepara solo testo: l’invio resta esplicito.</p>
    <label className="catalog-search">Cerca nel catalogo caricato<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Nome, categoria, marca, SKU, tag o ID…" /></label>
    {remote.error && <ErrorBox message={remote.error} retry={remote.reload} title="Catalogo non disponibile" />}
    {remote.loading && <p role="status">Caricamento prodotti…</p>}
    {remote.data && <p className="small-label">{products.length} di {remote.data.length} prodotti</p>}
    <div className="product-grid">{products.map((product) => <ProductCard key={product.id} product={product} onAsk={onAsk} showDetails />)}</div>
    {remote.data && products.length === 0 && <p className="muted">Nessun prodotto corrisponde alla ricerca.</p>}
  </section>;
}
