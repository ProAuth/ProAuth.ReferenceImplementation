import type { ReactNode } from 'react';
import type { Catalogue, Member } from './api';
import '../Workplace/src/styles.css';
export function Shell({
  title,
  catalogue,
  authenticated,
  member,
  error,
  children,
}: {
  title: string;
  catalogue?: Catalogue;
  authenticated: boolean;
  member?: Member;
  error: string;
  children: ReactNode;
}) {
  return (
    <div className="workspace">
      <aside>
        <a className="brand" href="/">
          {catalogue?.company.displayName || title}
        </a>
        <span className="eyebrow">{title}</span>
        <nav aria-label="Main">
          <a className="selected" href="#work">
            {title}
          </a>
          <a href="/api/identity">My verified identity</a>
        </nav>
        <p className="fiction">
          {catalogue?.company.notice || 'Fictional business environment for training.'}
        </p>
      </aside>
      <main>
        <header>
          <span>{title}</span>
          <a href={authenticated ? '/bff/account/logout' : '/bff/account/login?returnUrl=%2F'}>
            {authenticated ? 'Sign out' : 'Sign in'}
          </a>
        </header>
        <section className="intro">
          <h1>{title}</h1>
          {member && (
            <p>
              {member.id} ·{' '}
              {catalogue?.organizations.find((o) => o.id === member.organizationId)?.displayName ||
                member.organizationId}
            </p>
          )}
        </section>
        {error && (
          <p role="alert" className="message error">
            {error === 'membership_required'
              ? 'Your sign-in succeeded. The demo operator must assign business membership before you can continue.'
              : error}
          </p>
        )}
        {!authenticated && <p className="message">Sign in to access your assigned work.</p>}
        <section id="work">{children}</section>
        <footer>Mervikon Group</footer>
      </main>
    </div>
  );
}
